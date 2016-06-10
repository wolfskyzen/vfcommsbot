using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
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

        #region Implementation

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
        /// Tests to see if the given User is registered as an Admin of this Bot.
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public bool IsUserBotAdmin(User user)
        {
            if(    null == user
                || null == mSettings
                || null == mSettings.AdminUserList
                || false == mSettings.AdminUserList.Any()
                )
            {
                return false;
            }

            return mSettings.AdminUserList.Contains(user.Id);
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
        /// Trigger a save of the bot's settings file
        /// </summary>
        protected void Save()
        {
            if(null != mSettings)
            {
                Settings.Write(mSettings, SETTINGS_FILENAME);
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

                if(complete)
                {
                    mActiveMultistepCommands.Remove(msg.From.Id);
                }
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
            if(false == IsUserBotAdmin(msg.From))
            {
                return false;
            }

            switch (cmd)
            {
                case "adminadd":
                case "adminremove":
                {
                    string replyMessage = null;
                    string username = null;
                    for(int idx = 0; idx < msg.Entities.Count; idx++)
                    {
                        if(MessageEntityType.Mention == msg.Entities[idx].Type)
                        {
                            username = msg.EntityValues[idx];
                            break;
                        }
                    }

                    if(String.IsNullOrEmpty(username) || null == mSettings.NoticedUserList)
                    {
                        replyMessage = "Unable to find a username to add. Please include a username with the @ mention format.";
                    }
                    else
                    {
                        // Trim the @ off the start of the username mention format
                        string trimmedUsername = username.Substring(1).ToLower();
                        int userid = 0;
                        if(null != mSettings.NoticedUserList)
                        {
                            var result = mSettings.NoticedUserList.FirstOrDefault(kvp => kvp.Key.Equals(trimmedUsername));
                            if(false == String.IsNullOrEmpty(result.Key))
                            {
                                userid = result.Value;
                            }
                        }

                        if(0 == userid)
                        {
                            replyMessage = String.Format("Unable to find {0} in the userlist. Please get them to direct message the bot with /noticeme first.", username);
                        }
                        else
                        {
                            switch(cmd)
                            {
                                case "adminadd":
                                {
                                     if(null == mSettings.AdminUserList)
                                    {
                                        mSettings.AdminUserList = new List<int>();
                                    }

                                    if(false == mSettings.AdminUserList.Contains(userid))
                                    {
                                        mSettings.AdminUserList.Add(userid);
                                        Save();
                                    }

                                    replyMessage = String.Format("Added {0} to the admin list.", username);
                                }
                                break;

                                case "adminremove":
                                {
                                    if(null != mSettings.AdminUserList && mSettings.AdminUserList.Contains(userid))
                                    {
                                        mSettings.AdminUserList.Remove(userid);
                                        Save();

                                        replyMessage = String.Format("Removed {0} from the admin list.", username);
                                    }
                                    else
                                    {
                                        replyMessage = String.Format("Did not find {0} in the admin list.", username);
                                    }
                                }
                                break;
                            }
                        }
                    }

                    mTelegram.SendTextMessage(msg.Chat.Id, replyMessage);
                }
                break;

                case "clearmeetinglink":
                {
                    mSettings.MeetingLink = null;
                    Save();

                    mTelegram.SendTextMessage(msg.Chat.Id, "Meeting link has been cleared.");
                }
                break;

                case "save":
                {
                    Save();
                    mTelegram.SendTextMessage(msg.Chat.Id, "Forced settings to save to settings.txt");
                }
                return true;

                case "setmeetinglink":
                {
                    string linktext = null;
                    for(int idx = 0; idx < msg.Entities.Count; idx++)
                    {
                        if(MessageEntityType.Url == msg.Entities[idx].Type)
                        {
                            linktext = msg.EntityValues[idx];
                            break;
                        }
                    }

                    if(String.IsNullOrEmpty(linktext))
                    {
                        mTelegram.SendTextMessage(msg.Chat.Id, "Unable to find a valid weblink to share as the meeting link.");
                    }
                    else
                    {
                        mSettings.MeetingLink = linktext;
                        Save();

                        StringBuilder sb = new StringBuilder();
                        sb.AppendFormat("Meeting link has been set by {0} (@{1}):", msg.From.FirstName, msg.From.Username);
                        sb.AppendLine(linktext);
                        BroadcastMessage(sb.ToString(), true);
                    }
                }
                return true;

                case "setnextmeeting":
                {
                    SetNextMeetingMultistepCommand mscmd = new SetNextMeetingMultistepCommand();
                    mscmd.Start(msg);
                    mActiveMultistepCommands[mscmd.UserID] = mscmd;
                }
                return true;

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
                case "start":
                {
                    HandleCommandHelp(msg, isPrivateMessage, replyToMessageID);
                }
                break;

                case "nextmeeting":
                {
                    string text = DetermineNextMeetingFormattedString();
                    mTelegram.SendTextMessage(msg.Chat.Id, text, false, false, replyToMessageID);
                }
                return true;

                case "meetinglink":
                {
                    if(String.IsNullOrEmpty(mSettings.MeetingLink))
                    {
                        mTelegram.SendTextMessage(msg.Chat.Id, "Meeting link is not setup yet", false, false, replyToMessageID);
                    }
                    else
                    {
                        mTelegram.SendTextMessage(
                            msg.Chat.Id,
                            "Connect to the meeting here: " + mSettings.MeetingLink,
                            true,   // Do not want a web preview of the link!
                            false,
                            replyToMessageID
                            );
                    }
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
        /// Respond to the /help bot command
        /// </summary>
        /// <param name="msg"></param>
        private void HandleCommandHelp(Message msg, bool isPrivateMessage, int replyToMessageID)
        {
            // Use explicit formatting on this message, so we know how
            // this will look when recieved by the client.
            string text =
@"Valid commands for the VancouFur Communication Bot

/broadcast - Send a message to all VF Staff chatrooms at the same time. (Must be sent as a Direct Message.)
/help - Show this list of commands
/hashtags - Gives a link to the department hashtags.
/meetinglink - Gives the active meeting online link, when a staff meeting is happening.
/nextmeeting - Displays the date, time and location of the next staff meeting.";

            if(isPrivateMessage && IsUserBotAdmin(msg.From))
            {
                // Include admin-only command informatiojn
                text +=
@"

Admin commands. If you get this message, you can use these commands. Must be sent via direct message.

/adminadd - Adds a user to the admin list. Must include an @ mention of the user to add. Target user must also message the bot with /noticeme to get added to the internal userlist.
/adminremove - Adds a user to the admin list. Must include an @ mention of the user to add.
/clearmeetinglink - Clears the current remote meeting link.
/setmeetinglink - Set a valid weblink for remote meeting connection. Will broadcast to all groups when changed.
/setnextmeeting - Set the date, time and location of the next meeting. Will broadcast to all groups when changed.
/save - Force the bot to save its internal settings.
/whois - A test command that just replies to you.
";
            }

            // Disable the web preview for this
            mTelegram.SendTextMessage(msg.Chat.Id, text, true, false, replyToMessageID);
        }

        /// <summary>
        /// Creates a formatted string for the next meeting message.
        /// This includes the date, time, location and a warning as to how soon the next meeting is.
        /// </summary>
        /// <returns></returns>
        private string DetermineNextMeetingFormattedString()
        {
            StringBuilder sb = new StringBuilder();

            // TODO: Need to determine if we are within the two hour "meeting is NOW" window of time
            DateTime current = DateTime.Now;
            if (DateTime.Compare(mSettings.NextMeeting, current) < 0)
            {
                sb.Append("The next meeting date is not set.");
            }
            else
            {
                TimeSpan delta = mSettings.NextMeeting - current;

                sb.AppendFormat("The next meeting is {0}", mSettings.NextMeeting.ToString("f"));

                if (String.IsNullOrEmpty(mSettings.NextMeetingLocation))
                {
                    sb.Append(". ");
                }
                else
                {
                    sb.AppendFormat(" at {0}. ", mSettings.NextMeetingLocation);
                }

                if (delta.Days > 1)
                {
                    sb.AppendFormat("That is in {0} day{1}.", delta.Days, (delta.Days == 1 ? "" : "s"));
                }
                else if (delta.Hours > 0)
                {
                    sb.AppendFormat("That is in {0} hour{1} and {2} minute{3}.",
                        delta.Hours,
                        (delta.Hours == 1 ? "" : "s"),
                        delta.Minutes,
                        (delta.Minutes == 1 ? "" : "s")
                        );
                }
                else
                {
                    sb.AppendFormat("That is in {0} minute{1}", delta.Minutes, (delta.Minutes == 1 ? "!" : "s."));
                }
            }

            return sb.ToString();
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

                case "noticeme":
                {
                    bool contains = (null != mSettings.NoticedUserList && mSettings.NoticedUserList.ContainsKey(msg.From.Username.ToLower()));
                    if(contains)
                    {
                        mTelegram.SendTextMessage(msg.Chat.Id, "Senpai already noticed you.");
                    }
                    else
                    {
                        if(null == mSettings.NoticedUserList)
                        {
                            mSettings.NoticedUserList = new Dictionary<string, int>();
                        }
                        mSettings.NoticedUserList[msg.From.Username.ToLower()] = msg.From.Id;
                        Save();

                        mTelegram.SendTextMessage(msg.Chat.Id, "Senpai has noticed you.");
                    }
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

        #region Broadcasting Actions

        private void BroadcastMessage(string message, bool disableWebPagePreview = false)
        {
            if(    null == mTelegram
                || String.IsNullOrEmpty(message)
                || null == mSettings.BroadcastGroupList
                || false == mSettings.BroadcastGroupList.Any()
                )
            {
                return;
            }

            foreach(var groupid in mSettings.BroadcastGroupList)
            {
                mTelegram.SendTextMessage(groupid, message, disableWebPagePreview);
            }
        }

        public void BroadcastMessageFromUser(User user, string message)
        {
            if(null == user ||String.IsNullOrEmpty(message))
            {
                return;
            }

            string fullMessageText = String.Format("Message from {0} (@{1}):\n", user.FirstName, user.Username) + message;
            BroadcastMessage(fullMessageText);
        }

        public void SetNextMeeting(User user, DateTime datetime, string location)
        {
            if(null == mSettings)
            {
                return;
            }

            mSettings.NextMeeting = datetime;
            mSettings.NextMeetingLocation = location;
            Save();

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Next meeting has been set by {0} (@{1}).", user.FirstName, user.Username);
            sb.AppendLine();
            sb.AppendLine(DetermineNextMeetingFormattedString());
            BroadcastMessage(sb.ToString());
        }

        #endregion
    }
}
