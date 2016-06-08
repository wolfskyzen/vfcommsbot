using System;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace vfcommsbot
{
    public class BroadcastMultistepCommand : MultistepCommandInterface
    {
        public long ChatID { get; protected set; }
        public int UserID
        {
            get
            {
                if(null != mTargetUser)
                {
                    return mTargetUser.Id;
                }

                return 0;
            }
        }

        private User mTargetUser = null;

        public BroadcastMultistepCommand()
        {
            // Nothing needed here
        }

        public void Cancel(Message msg)
        {
            CommunicationBot.Telegram.SendTextMessage(
                ChatID,
                "Your message broadcast has been cancelled."
                );
        }

        public void Start(Message msg)
        {
            // Required values that need to be store
            ChatID = msg.Chat.Id;
            mTargetUser = msg.From;

            CommunicationBot.Telegram.SendTextMessage(
                ChatID,
                "Please tell me the message you wish to broadcast to ALL VF Staff chatrooms. Or message me with /cancel to stop the broadcast."
                );
        }

        public bool Update(Message msg)
        {
            CommunicationBot.Instance.BroadcastMessageFromUser(mTargetUser, msg.Text);

            // Just so the user gets a confirmation that it tried to send the broadcasts
            // We cannot guarentee that there are any white list groups setup for the bot.
            Api telegram = CommunicationBot.Telegram;
            telegram.SendTextMessage(ChatID, "Your message has been broadcast to ALL VF Staff chatrooms.");

            return true;
        }
    }
}
