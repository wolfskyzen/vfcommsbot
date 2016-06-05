using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace vfcommsbot
{
    /// <summary>
    /// VancouFur Telegram Communications Bot
    /// 2016 (c) Zen
    /// </summary>
    class Program
    {
        private static CommunicationBot mBot = null;

         /// <summary>
        /// Picks up the cancel command from the console window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if(null != mBot)
            {
                // Tell the main thead to stop
                mBot.Cancel();
            }

            // Do not process further!
            e.Cancel = true;
        }

        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            Console.Title = "VancouFur Communications Bot";
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                // Setup the bot
                mBot = new CommunicationBot();

                // MAIN BLOCKING UPDATE
                mBot.Run();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Error running the communications bot: " + ex.ToString());
            }

            // Cleanup the bot, if it was created
            if(null != mBot)
            {
                mBot.Stop();
            }
        }
    }
}
