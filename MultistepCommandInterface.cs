using System;
using Telegram.Bot.Types;

namespace vfcommsbot
{
    /// <summary>
    /// Interface for handling commands that require mode than one input from a user.
    /// Tracking of these is done by the bot core.
    /// 
    /// Multistep commands are direct message only and each user can have only one active
    /// command at a time.
    /// </summary>
    public interface MultistepCommandInterface
    {
        /// <summary>
        /// Required Chat ID to track which telegram chat to send messages to.
        /// </summary>
        long ChatID { get; }

        /// <summary>
        /// Required User ID to track the owner of this command.
        /// </summary>
        int UserID { get; }

        /// <summary>
        /// Immediately end the current multistep command.
        /// Triggered by a /cancel command.
        /// </summary>
        /// <param name="msg"></param>
        void Cancel(Message msg);

        /// <summary>
        /// Start the multistep command for the current user
        /// </summary>
        /// <param name="msg"></param>
        void Start(Message msg);

        /// <summary>
        /// Updates the current multistep command with a new message from the target user.
        /// <param name="msg"></param>
        /// <returns>True when the command is done all steps, false if not.</returns>
        bool Update(Message msg);
    }
}
