using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace vfcommsbot
{
    public class Settings
    {
        /// <summary>
        /// List of user IDs for admin users
        /// These users are allowed to access admin-level commands
        /// </summary>
        [DefaultValue(null)]
        public List<int> AdminUserList { get; set; }

        /// <summary>
        /// Telegram Bot Token, provided by the @BotFather upon bot creation
        /// </summary>
        [DefaultValue(null)]
        public string BotToken { get; set; }

        /// <summary>
        /// List of group chat IDs as the white list for the broadcast command.
        /// All of the groups in this list get a message when /broadcast is used.
        /// </summary>
        [DefaultValue(null)]
        public List<long> BroadcastGroupList { get; set; }

        /// <summary>
        /// Date and time of the next staff meeting
        /// </summary>
        [DefaultValue(null)]
        public DateTime NextMeeting { get; set; }

        /// <summary>
        /// Location of the next staff meeting
        /// </summary>
        [DefaultValue("")]
        public string NextMeetingLocation { get; set; }

        #region File Accessors

        public static Settings Read(string filename)
        {
            Settings settings = null;

            try
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.NullValueHandling = NullValueHandling.Ignore;
                serializer.MissingMemberHandling = MissingMemberHandling.Ignore;
                serializer.Formatting = Formatting.Indented;

                using (StreamReader sr = new StreamReader(filename))
                {
                    using (JsonReader reader = new JsonTextReader(sr))
                    {
                        settings = serializer.Deserialize<Settings>(reader);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Failed to read Settings from " + filename);
                Console.WriteLine(ex.ToString());
            }

            return settings;
        }

        public static void Write(Settings settings, string filename)
        {
            try
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.NullValueHandling = NullValueHandling.Ignore;
                serializer.Formatting = Formatting.Indented;

                using (StreamWriter sw = new StreamWriter(filename))
                {
                    using (JsonWriter writer = new JsonTextWriter(sw))
                    {
                        serializer.Serialize(writer, settings);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Failed to write Settings to " + filename);
                Console.WriteLine(ex.ToString());
            }
        }

        #endregion
    }
}
