using System;
using System.Text;
using Telegram.Bot.Types;

namespace vfcommsbot
{
    public static class Utilities
    {
        /// <summary>
        /// Deteremines if there is a bot command string in the message entities
        /// and extracts the non-tokenized string from them.
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static string DetermineCommandStringFromMessage(Message msg)
        {
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
                return null;
            }

            // Clean the command up to a simple and easier to use string
            cmd = Utilities.ParseCommandFromString(cmd);
            if(String.IsNullOrEmpty(cmd))
            {
                return null;
            }

            return cmd;
        }

        /// <summary>
        /// Log the details of a Message API object to the console
        /// </summary>
        /// <param name="msg"></param>
        public static void LogMessage(Message msg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MESSAGE:");
            if(ChatType.Private != msg.Chat.Type)
            {
                sb.AppendLine(String.Format("Group: {0} ({1})", msg.Chat.Title, msg.Chat.Id));
            }
            sb.AppendLine(String.Format("From: @{0} {1}", msg.From.Username, msg.From.Id));
            sb.AppendLine("Time: " + msg.Date.ToLocalTime().ToString("yyyy-MM-dd hh:MM tt (zz)"));
            sb.AppendLine(msg.Text);

            for(int idx = 0; idx < msg.Entities.Count; idx++)
            {
                sb.AppendLine(String.Format(" - {0} {1}", msg.Entities[idx].Type.ToString(), msg.EntityValues[idx]));
            }

            Console.WriteLine(sb.ToString());  
        }

        /// <summary>
        /// Log the details of a User API object to the console
        /// </summary>
        /// <param name="user"></param>
        public static void LogUser(User user)
        {
            Console.WriteLine(String.Format("USER: @{0} {1} ({2} {3})",
                user.Username,
                user.Id,
                user.FirstName,
                user.LastName
                ));
        }

        /// <summary>
        /// Parses a string looking for a / prefixed command
        /// </summary>
        /// <param name="text"></param>
        /// <returns>
        ///  - Lower-case command string name.
        ///  - String.Empty if nothing is found
        /// </returns>
        public static string ParseCommandFromString(string text)
        {
            if(String.IsNullOrEmpty(text))
            {
                return String.Empty;
            }

            string worktext = text.Trim().ToLower();
            if(worktext.Length <= 1)
            {
                return String.Empty;
            }

            if(worktext.StartsWith("/"))
            {
                // Check for /command@BotName, which happens with bot command auto-completing
                int index = worktext.IndexOf('@');
                if(index > 0 && index < worktext.Length)
                {
                    return worktext.Substring(1, index-1);
                }

                // Check for /command text cases
                index = worktext.IndexOf(' ');
                if(index > 0 && index < worktext.Length)
                {
                    return worktext.Substring(1, index-1);
                }

                // Assume this is just /command
                return worktext.Substring(1);
            }

            return String.Empty;
        }
    }
}
