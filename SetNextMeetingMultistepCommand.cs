using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace vfcommsbot
{
    public class SetNextMeetingMultistepCommand : MultistepCommandInterface
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

        private DateTime mDateTime;
        private string mLocation = null;
        private User mTargetUser = null;

        private enum eState
        {
            DATETIME,
            LOCATION,
            CONFIRMATION,
            DONE
        };
        private eState mState = eState.DONE;

        public void Cancel(Message msg)
        {
            CommunicationBot.Telegram.SendTextMessage(
                ChatID,
                "Set next meeting has been cancelled.",
                false,
                false,
                0,
                (eState.CONFIRMATION == mState ? new ReplyKeyboardHide() : null)
                );
        }

        public void Start(Message msg)
        {
            // Required values that need to be store
            ChatID = msg.Chat.Id;
            mTargetUser = msg.From;

            mState = eState.DATETIME;
            SendStateMessage();
        }

        public bool Update(Message msg)
        {
            string error = null;
            switch(mState)
            {
                case eState.DATETIME:
                {
                    if(false == String.IsNullOrEmpty(msg.Text))
                    {
                        if(DateTime.TryParse(msg.Text, out mDateTime))
                        {
                            mState = eState.LOCATION;
                        }
                        else
                        {
                            error = "Unable to determine the date and time from your message.";
                        }
                    }
                    else
                    {
                        error = "Unable to determine the date and time from your message.";
                    }
                }
                break;

                case eState.LOCATION:
                {
                    if(String.IsNullOrEmpty(msg.Text))
                    {
                        error = "Invalid location.";
                    }
                    else
                    {
                        mLocation = msg.Text;
                        mState = eState.CONFIRMATION;
                    }
                }
                break;

                case eState.CONFIRMATION:
                {
                    if(msg.Text.Equals("Yes", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Save the settings and broadcast the change
                        CommunicationBot.Instance.SetNextMeeting(mTargetUser, mDateTime, mLocation);

                        // We are done!
                        mState = eState.DONE;
                    }
                    else
                    {
                        // Start over!
                        mState = eState.DATETIME;
                    }
                }
                break;
            }

            SendStateMessage(error);

            return (mState == eState.DONE);
        }

        private void SendStateMessage(string error = null)
        {
            StringBuilder sb = new StringBuilder();

            if(false == String.IsNullOrEmpty(error))
            {
                sb.AppendLine(error);
                sb.AppendLine();
            }
            
            switch(mState)
            {
                case eState.DATETIME:
                    sb.AppendLine("Please message me with the date and time of the next meeting.");
                break;

                case eState.LOCATION:
                    sb.AppendLine("Please message me with the location of the next meeting.");
                break;

                case eState.CONFIRMATION:
                {
                    sb.AppendLine(String.Format("The next meeting is {0} at {1}.", mDateTime.ToString("f"), mLocation));
                    sb.AppendLine("Is this correct?");

                    // Make use of a custom bot keyboard, which requries a custom send text message
                    KeyboardButton[] buttons = new KeyboardButton[]{ "No", "Yes" };
                    ReplyKeyboardMarkup markup = new ReplyKeyboardMarkup(buttons, true, true);
                    CommunicationBot.Telegram.SendTextMessage(
                        ChatID,
                        sb.ToString(),
                        false,
                        false,
                        0,
                        markup
                        );
                }
                return;

                case eState.DONE:
                    return;
            }

            CommunicationBot.Telegram.SendTextMessage(ChatID, sb.ToString());
        }
    }
}
