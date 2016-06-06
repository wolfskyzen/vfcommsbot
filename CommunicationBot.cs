using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace vfcommsbot
{
    public class CommunicationBot
    {
        #region Member Variables

        private static readonly string SETTINGS_FILENAME = "settings.txt";

        // Sending this as the message ID sends the message without making it a reply
        private static readonly int NO_REPLY_MESSAGE_ID = 0;

        /// <summary>
        /// Cached version of the bot's user information.
        /// </summary>
        private User mBotUser = null;

        /// <summary>
        /// Reactive subject polling for handling mesages one at a time
        /// instead of in a giant block during the main update.
        /// </summary>
        private Subject<Message> mMessages = new Subject<Message>();
        private IDisposable mMessageSubscription = null;

        /// <summary>
        /// Map of all active mutlistep commands, keyed by User ID.
        /// </summary>
        private Dictionary<int, MultistepCommandInterface> mActiveMultistepCommands = new Dictionary<int, MultistepCommandInterface>();

        /// <summary>
        /// Offset used by the Telegram API to get messages in batches
        /// </summary>
        private int mOffset = 0;

        /// <summary>
        /// Singleton instance handle for the bot
        /// </summary>
        private static CommunicationBot mInstance = null;
        public static CommunicationBot Instance
        {
            get { return mInstance; }
        }

        /// <summary>
        /// Used to keep the main thread alive.
        /// When this is false the bot will stop updating.
        /// </summary>
        private bool mRunning = false;

        /// <summary>
        /// Settings and values stored between bot instances
        /// </summary>
        private Settings mSettings = null;

        /// <summary>
        /// Handle to the Telegram Bot API interactions
        /// </summary>
        private static Api mTelegram = null;

        /// <summary>
        /// Public accessor for the current Telegram API connection
        /// </summary>
        public static Api Telegram
        {
            get { return mTelegram; }
        }

        #endregion

        #region Singleton Interface

        /// <summary>
        /// Tells the Bot to stop updating.
        /// </summary>
        public static void Cancel()
        {
            if(null != mInstance)
            {
                Console.WriteLine("Cancelling Bot");

                mInstance.mRunning = false;
            }
        }

        /// <summary>
        /// Creates and sets up the bot instance.
        /// Can call the BLOCKING Run() command to start updating.
        /// </summary>
        public static void Create()
        {
            if(null == mInstance)
            {
                mInstance = new CommunicationBot();
            }
        }

        /// <summary>
        /// Stops and shuts down the bot instance.
        /// This will clear and clean up the bot singleton.
        /// </summary>
        public static void Destroy()
        {
            if (null != mInstance)
            {
                mInstance.Stop();
            }

            mInstance = null;
        }

        /// <summary>
        /// Main BLOCKING update call.
        /// This controls the update and lets the bot run.
        /// 
        /// Throws all exceptions.
        /// </summary>
        public static void Run()
        {
            if (null != mInstance)
            {
                // MAIN BLOCKING UPDATE
                mInstance.UpdateBlocking();
            }
        }

        #endregion

        #region Main Implementation

        /// <summary>
        /// Constructor
        /// </summary>
        protected CommunicationBot()
        {
            mSettings = Settings.Read(SETTINGS_FILENAME);
            if(null == mSettings)
            {
                throw new NullReferenceException("Unable to load " + SETTINGS_FILENAME);
            }

            mTelegram = new Api(mSettings.BotToken);

            mMessageSubscription = mMessages.Subscribe(msg => HandleMessage(msg));
        }

        /// <summary>
        /// Tells the Bot that it is done and can cleanup
        /// </summary>
        protected void Stop()
        {
            if(mRunning)
            {
                Cancel();
            }

            Console.WriteLine("Stopping Bot");

            if(null != mMessageSubscription)
            {
                mMessageSubscription.Dispose();
            }
        }

        /// <summary>
        /// Main BLOCKING thread call. This runs the bot update loop.
        /// </summary>
        protected void UpdateBlocking()
        {
            Console.WriteLine("Starting Bot");

            // Check the bot's ME status first
            try
            {
                var task = mTelegram.GetMe();
                task.Wait();
                mBotUser = task.Result;
                Utilities.LogUser(mBotUser);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Failed GetMe(): " + ex.ToString());
            }

            mRunning = true;
            while(mRunning)
            {
                try
                {
                    var updates = mTelegram.GetUpdates(mOffset, 100, 10);

                    try
                    {
                        updates.Wait();
                    }
                    catch(AggregateException)
                    {
                        // Happens when the timeout completes
                        Thread.Yield();
                        continue;
                    }

                    if(updates.IsCanceled)
                    {
                        Thread.Yield();
                        continue;
                    }

                    var results = updates.Result;
                    if(null != results && results.Any())
                    {
                        int nextoffset = mOffset;

                        foreach(var update in results)
                        {
                            if(null == update.Message)
                            {
                                continue;
                            }

                            // Only parse text messages intentionally
                            // No reason to parse data for stickers, images, etc
                            if(MessageType.TextMessage == update.Message.Type)
                            {
                                mMessages.OnNext(update.Message);
                            }

                            nextoffset = update.Id + 1;
                        }

                        mOffset = nextoffset;
                    }

                    Thread.Yield();
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Error during updates: {0}", ex.ToString());
                    Cancel();
                }
            }
        }

        #endregion

        #region Message Handling

        /// <summary>
        /// Root handler for determining how to deal with this text message
        /// </summary>
        /// <param name="msg"></param>
        private void HandleMessage(Message msg)
        {
            Utilities.LogMessage(msg);

            MultistepCommandInterface mscmd = null;

            // Multistep commands only work with private messages
            if(ChatType.Private == msg.Chat.Type)
            {
                if(mActiveMultistepCommands.ContainsKey(msg.From.Id))
                {
                    mscmd = mActiveMultistepCommands[msg.From.Id];
                }
            }

            // Both single and multistep commands need the command string
            // to determine how to respond to it.
            string cmd = Utilities.DetermineCommandStringFromMessage(msg);

            if(null != mscmd)
            {
                bool complete = false;

                if(false == String.IsNullOrEmpty(cmd) && cmd.Equals("cancel"))
                {
                    mscmd.Cancel(msg);
                    complete = true;
                }
                else
                {
                    complete = mscmd.Update(msg);
                }

                mActiveMultistepCommands.Remove(msg.From.Id);
            }
            else if(false == String.IsNullOrEmpty(cmd))
            {
                // Pass through common handling first
                if(HandleCommonCommand(msg, cmd))
                {
                    return;
                }

                // Allow for message-type specific handling
                if(ChatType.Private == msg.Chat.Type)
                {
                    HandleDirectCommand(msg, cmd);
                }
                else
                {
                    HandleGroupCommand(msg, cmd);
                }
            }
        }

        /// <summary>
        /// Process direct message commands that come from the white-listed admin users.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="cmd"></param>
        /// <returns>True if the message was handled here, false if not.</returns>
        private bool HandleAdminCommand(Message msg, string cmd)
        {
            if(null == mSettings.AdminUserList || false == mSettings.AdminUserList.Contains(msg.From.Id))
            {
                return false;
            }

            switch (cmd)
            {
                case "whois":
                {
                    string text = String.Format(
                        "Hello {0}, I am {1}, the Telegram communication bot for VancouFur staff!",
                        msg.From.FirstName,
                        (null != mBotUser ? mBotUser.FirstName : "Unknown")
                        );

                    mTelegram.SendTextMessage(msg.Chat.Id, text);
                }
                return true;

                case "save":
                {
                    Settings.Write(mSettings, SETTINGS_FILENAME);

                    mTelegram.SendTextMessage(msg.Chat.Id, "Forced settings to save to settings.txt");
                }
                return true;

                case "setnextmeeting":
                {
                    string replyMessage = null;

                    int firstSpace = msg.Text.IndexOf(' ');
                    if(firstSpace >= 0 && firstSpace < msg.Text.Length)
                    {
                        string dttext = msg.Text.Substring(firstSpace + 1);

                        DateTime dt;
                        if(DateTime.TryParse(dttext, out dt))
                        {
                            // TODO: Turn this into a group chat broadcast!
                            replyMessage = String.Format("Next meeting set to {0} by {1}", dt, msg.From.FirstName);

                            mSettings.NextMeeting = dt;
                            Settings.Write(mSettings, SETTINGS_FILENAME);
                        }
                        else
                        {
                            replyMessage = String.Format("Unable to determine next meeting from: {0}", dttext);
                        }
                    }
                    else
                    {
                        replyMessage = String.Format("Unable to determine the next meeting date from your message.");
                    }

                    mTelegram.SendTextMessage(msg.Chat.Id, replyMessage);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Process commands that work for both direct messages and group chats
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="cmd"></param>
        /// <returns></returns>
        private bool HandleCommonCommand(Message msg, string cmd)
        {
            bool isPrivateMessage = (ChatType.Private == msg.Chat.Type);
            int replyToMessageID = (isPrivateMessage ? NO_REPLY_MESSAGE_ID : msg.MessageId);

            switch(cmd)
            {
                case "help":
                {
                    // Use explicit formatting on this message, so we know how
                    // this will look when recieved by the client.
                    string text =
@"Valid commands for the VancouFur Communication Bot:

/broadcast - Send a message to all VF Staff chatrooms at the same time. (Must be sent as a Direct Message.)
/help - Show this list of commands
/hashtags - Gives a link to the department hashtags.
/meetinglink - Gives the active meeting online link, when a staff meeting is happening.
/nextmeeting - Displays the date, time and location of the next staff meeting.";

                    // Disable the web preview for this
                    mTelegram.SendTextMessage(msg.Chat.Id, text, true, false, replyToMessageID);
                }
                break;

                case "nextmeeting":
                {
                    string text = null;

                    // TODO: Show a different message, with the meeting link, if the current time is within
                    // two hours of the meeting start time
                    if (null != mSettings.NextMeeting && DateTime.Compare(mSettings.NextMeeting, DateTime.Now) >= 0)
                    {
                        text = String.Format("Next meeting is {0}", mSettings.NextMeeting.ToString("f"));
                    }
                    else
                    {
                        text = "Next meeting is not set";
                    }

                    mTelegram.SendTextMessage(msg.Chat.Id, text, false, false, replyToMessageID);
                }
                return true;

                case "meetinglink":
                {
                    mTelegram.SendTextMessage(msg.Chat.Id, "Meeting link is not setup yet", false, false, replyToMessageID);
                }
                return true;

                case "hashtag":
                case "hashtags":
                case "tags":
                {
                    mTelegram.SendTextMessage(
                        msg.Chat.Id,
                        "A list of all department hashtags is here:\nhttps://docs.google.com/spreadsheets/d/1APCkfnCPO6KDSYGMAo6U7o3EDCw_j0R1s5RpAcSGpjo",
                        false,
                        false,
                        replyToMessageID
                        );
                }
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Process messages that only work from direct messages
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="cmd"></param>
        private void HandleDirectCommand(Message msg, string cmd)
        {
            if(HandleAdminCommand(msg, cmd))
            {
                return;
            }

            // Commands only from direct messages
            switch(cmd)
            {
                case "broadcast":
                {
                    BroadcastMultistepCommand mscmd = new BroadcastMultistepCommand();
                    mscmd.Start(msg);
                    mActiveMultistepCommands[mscmd.UserID] = mscmd;
                }
                break;
            }
        }

        /// <summary>
        /// Process commands 
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="cmd"></param>
        private void HandleGroupCommand(Message msg, string cmd)
        {
            // TODO: Handle and track hashtags
        }

        #endregion

        #region Group Messaging

        public void BroadcastMessage(User user, string message)
        {
            if(    null == user
                || null == mTelegram
                || String.IsNullOrEmpty(message)
                || null == mSettings.BroadcastGroupList
                || false == mSettings.BroadcastGroupList.Any()
                )
            {
                return;
            }

            string fullMessageText = String.Format("Message from {0} (@{1}):\n", user.FirstName, user.Username) + message;
            foreach(var groupid in mSettings.BroadcastGroupList)
            {
                mTelegram.SendTextMessage(groupid, fullMessageText);
            }
        }

        #endregion
    }
}
