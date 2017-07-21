using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AdiIRCAPIv2.Arguments.Channel;
using AdiIRCAPIv2.Arguments.ChannelMessages;
using AdiIRCAPIv2.Arguments.ChannelModes;
using AdiIRCAPIv2.Arguments.Contextless;
using AdiIRCAPIv2.Enumerators;
using AdiIRCAPIv2.Interfaces;

// Include the API

namespace StripSystem
{
    public class Filter : IPlugin // Reference this is a plugin
    {
        public IPluginHost Host { get; set; }

        public string Name => "Join-Part Filter";

        public string Description => "Filters join/part messages.";

        public string Author => "Xesyto";    
        public string Version => "4";      
        public string Email => "s.oudenaarden@gmail.com";
               
        private Dictionary<string, UserData> userDatabase;
        private readonly string ColourCode = "8"; //ascii char 3 is included at the start of this string but not clearly visible in VS Editor

        public Filter()
        {
            userDatabase = new Dictionary<string, UserData>();
        }

        public void Initialize()
        {
            // This is called when the plugin is loaded
            // Suscribe to delegates here

            Host.OnChannelJoin += OnChannelJoin;
            Host.OnChannelPart += OnChannelPart;
            Host.OnQuit += OnQuit;
            Host.OnChannelNormalMessage += OnChannelNormalMessage;
            Host.OnNick += OnNick;
            Host.OnChannelMode += OnChannelMode;
        }



        private void OnChannelNormalMessage(ChannelNormalMessageArgs argument)
        {                        
            //sometimes ( mostly twitch ) user ident or host is not yet known even though a message was received
            if (argument.User == null || argument.User.Ident == null || argument.User.Host == null)
                return;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            if (!userDatabase.ContainsKey(userKey))
            {
                var newData = new UserData { AnnouncedJoin = true, LastMessage = DateTime.Now };
                userDatabase.Add(userKey, newData);
            }
            else
            {
                var userData = userDatabase[userKey];

                if (!userData.AnnouncedJoin)
                {
                    var user = argument.User;
                    var channel = argument.Channel;
                    var message = argument.Message;

                    var newMessage = $":{user.Nick}!{user.Ident}@{user.Host} PRIVMSG {channel.Name} :{message} {ColourCode}(logged in {userData.TimeSinceJoin()} ago)";
                    argument.Server.SendFakeRaw(newMessage);

                    userData.AnnouncedJoin = true;
                }

                userData.LastMessage = DateTime.Now;
            }
        }

        private void OnChannelJoin(ChannelJoinArgs argument)
        {
            argument.EatData = EatData.EatText;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            if (userDatabase.ContainsKey(userKey))
            {
                var userData = userDatabase[userKey];
                if (userData.TalkedRecently())
                {
                    argument.EatData = EatData.EatNone;
                }
                userData.Rejoined();
            }
            else
            {
                var userData = new UserData { Joined = DateTime.Now };

                userDatabase.Add(userKey, userData);
            }
        }

        private void OnChannelPart(ChannelPartArgs argument)
        {
            argument.EatData = EatData.EatText;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            if (!userDatabase.ContainsKey(userKey)) return;

            var userData = userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                argument.EatData = EatData.EatNone;
            }
        }

        private void OnQuit(QuitArgs argument)
        {
            argument.EatData = EatData.EatText;

            foreach (IChannel channel in argument.Server.GetChannels)
            {
                var userKey = argument.Server + channel.Name + argument.User.Ident + argument.User.Host;

                if (!userDatabase.ContainsKey(userKey)) continue;

                var userData = userDatabase[userKey];
                if (!userData.TalkedRecently()) continue;

                argument.EatData = EatData.EatNone;
                return;
            }
        }

        private void OnNick(NickArgs argument)
        {
            //argument.EatData = EatData.EatAll;
            
            foreach (IChannel channel in argument.Server.GetChannels)
            {
                var userKey = argument.Server + channel.Name + argument.User.Ident + argument.User.Host;

                if (!userDatabase.ContainsKey(userKey)) continue;

                var userData = userDatabase[userKey];
                if (!userData.TalkedRecently()) continue;

                //argument.EatData = EatData.EatNone;
                return;
            }
        }


        private void OnChannelMode(ChannelModeArgs argument)
        {
            var user = argument.User;
            var mode = argument.Mode;

            if (string.IsNullOrEmpty(user?.Ident))
            {
                var modeRegex = @"[+-]. (\w+)";
                var modeResult = Regex.Match(mode, modeRegex);

                string userName;

                if (modeResult.Success)
                {
                    userName = modeResult.Groups[1].ToString();
                }
                else
                {
                    argument.EatData = EatData.EatNone;
                    return;
                }

                foreach (IUser channelUser in argument.Channel.GetUsers)
                {
                    if (channelUser.Nick == userName)
                    {
                        user = channelUser as IChannelUser;
                    }
                }

                if (string.IsNullOrEmpty(user?.Ident))
                {
                    //Twitch has a nasty habit of showing mode removal after a user has left the channel. 
                    if (!argument.Server.Network.ToLower().Contains("twitch"))
                    {
                        argument.EatData = EatData.EatNone;
                        return;
                    }
                }
            }

            argument.EatData = EatData.EatText;
            var userKey = argument.Server.Network + argument.Channel.Name + user.Ident + user.Host;

            if (!userDatabase.ContainsKey(userKey)) return;

            var userData = userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                argument.EatData = EatData.EatNone;
            }
        }

        private void OnMode(IServer server, IChannel channel, IUser user, string mode, out EatData Return)
        {
            if (string.IsNullOrEmpty(user?.Ident))
            {
                var modeRegex = @"[+-]. (\w+)";
                var modeResult = Regex.Match(mode, modeRegex);

                string userName;

                if (modeResult.Success)
                {
                    userName = modeResult.Groups[1].ToString();
                }
                else
                {
                    Return = EatData.EatNone;
                    return;
                }

                foreach (IUser channelUser in channel.GetUsers)
                {
                    if (channelUser.Nick == userName)
                    {
                        user = channelUser;
                    }
                }

                if (string.IsNullOrEmpty(user?.Ident))
                {
                    //Twitch has a nasty habit of showing mode removal after a user has left the channel. 
                    if (!server.Network.ToLower().Contains("twitch"))
                    {
                        Return = EatData.EatNone;
                        return;
                    }                        
                }
            }

            Return = EatData.EatText;
            var userKey = server.Network + channel.Name + user.Ident + user.Host;

            if (!userDatabase.ContainsKey(userKey)) return;

            var userData = userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                Return = EatData.EatNone;
            }
        }

        private void OnNick(IServer server, IUser user, string newNick, out EatData Return)
        {
            Return = EatData.EatText;
        }

        private void OnQuit(IServer server, IUser user, string data, out EatData Return)
        {
            Return = EatData.EatText;

        }

        private void OnPart(IServer server, IChannel channel, IUser user, string partMessage, out EatData Return)
        {
            Return = EatData.EatText;
          
        }

        private void OnJoin(IServer server, IChannel channel, IUser user, out EatData Return)
        {
            Return = EatData.EatText;

            
        }

        private void OnMessage(IServer server, IChannel channel, IUser user, string message, out EatData Return)
        {            
            Return = EatData.EatNone;


        }

        public void Dispose()
        {
        }
    }
}