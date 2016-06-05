using System;
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
        /// Reactive subject polling for handling mesages one at a time
        /// instead of in a giant block during the main update.
        /// </summary>
        private Subject<Message> mMessages = new Subject<Message>();
        private IDisposable mMessageSubscription = null;

        /// <summary>
        /// Offset used by the Telegram API to get messages in batches
        /// </summary>
        private int mOffset = 0;

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
        /// Cached version of the bot's user information.
        /// </summary>
        private User mBotUser = null;

        #endregion

        #region Public Interface

        /// <summary>
        /// Constructor
        /// </summary>
        public CommunicationBot()
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
        /// Tells the Bot to stop updating.
        /// </summary>
        public void Cancel()
        {
            Console.WriteLine("Cancelling Bot");

            mRunning = false;
        }

        /// <summary>
        /// Main BLOCKING thread call. This runs the bot update loop
        /// </summary>
        public void Run()
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

        /// <summary>
        /// Tells the Bot that it is done and can cleanup
        /// </summary>
        public void Stop()
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

        #endregion

        #region Message Handling

        /// <summary>
        /// Root handler for determining how to deal with this text message
        /// </summary>
        /// <param name="msg"></param>
        private void HandleMessage(Message msg)
        {
            Utilities.LogMessage(msg);

            // Search for the first BotCommand entity type and extract that
            string cmd = String.Empty;
            for(int idx = 0; idx < msg.Entities.Count; idx++)
            {
                if(MessageEntityType.BotCommand == msg.Entities[idx].Type)
                {
                    cmd = msg.EntityValues[idx];
                    break;
                }
            }

            // No string? No command
            if(String.IsNullOrEmpty(cmd))
            {
                return;
            }

            // Clean the command up to a simple and easier to use string
            cmd = Utilities.ParseCommandFromString(cmd);
            if(String.IsNullOrEmpty(cmd))
            {
                return;
            }

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

        /// <summary>
        /// Process commands that work for both direct messages and group chats
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="cmd"></param>
        /// <returns></returns>
        private bool HandleCommonCommand(Message msg, string cmd)
        {
            bool isPrivateMessage = (ChatType.Private == msg.Chat.Type);
            int targetMessageId = (isPrivateMessage ? msg.MessageId : NO_REPLY_MESSAGE_ID);

            switch(cmd)
            {
                case "help":
                {
                    // Use explicit formatting on this message, so we know how
                    // this will look when recieved by the client.
                    string text =
@"Valid commands for the VancouFur Communication Bot:

/help - Show this list of commands
/hashtags - Gives a link to the department hashtags.
/meetinglink - Gives the active meeting online link, when a staff meeting is happening.
/nextmeeting - Displays the date, time and location of the next staff meeting.";

                    // Disable the web preview for this
                    mTelegram.SendTextMessage(msg.Chat.Id, text, true);
                }
                break;

                case "nextmeeting":
                {
                    string text = null;

                    if (null != mSettings.NextMeeting && DateTime.Compare(mSettings.NextMeeting, DateTime.Now) >= 0)
                    {
                        // TODO: Check to see if this is still a valid date
                        text = String.Format("Next meeting is {0}", mSettings.NextMeeting);
                    }
                    else
                    {
                        text = "Next meeting is not set";
                    }

                    mTelegram.SendTextMessage(msg.Chat.Id, text);
                }
                return true;

                case "meetinglink":
                {
                    mTelegram.SendTextMessage(msg.Chat.Id, "Meeting link is not setup yet");
                }
                return true;

                case "hashtag":
                case "hashtags":
                case "tags":
                {
                    mTelegram.SendTextMessage(
                        msg.Chat.Id,
                        "A list of all department hashtags is here:\nhttps://docs.google.com/spreadsheets/d/1APCkfnCPO6KDSYGMAo6U7o3EDCw_j0R1s5RpAcSGpjo"
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
            if(null != mSettings.AdminUserList && mSettings.AdminUserList.Contains(msg.From.Id))
            {
                // ADMIN-ONLY COMMANDS
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
                    break;

                    case "save":
                    {
                        Settings.Write(mSettings, SETTINGS_FILENAME);

                        mTelegram.SendTextMessage(msg.Chat.Id, "Forced settings to save to settings.txt");
                    }
                    break;

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
                    break;

                    case "shutdown":
                    {
                        // Can cause problems on the next launch, if this message is not flushed correctly
                        /*
                        mTelegram.SendTextMessage(msg.Chat.Id, "Shutting down");

                        Cancel();
                        */
                    }
                    break;
                }
            }

            // Commands only from direct messages
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
    }
}
