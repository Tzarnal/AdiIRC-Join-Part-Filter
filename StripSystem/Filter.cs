using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AdiIRCAPIv2.Arguments.Channel;
using AdiIRCAPIv2.Arguments.ChannelMessages;
using AdiIRCAPIv2.Arguments.ChannelModes;
using AdiIRCAPIv2.Arguments.Contextless;
using AdiIRCAPIv2.Enumerators;
using AdiIRCAPIv2.Interfaces;

namespace JoinPartFilter
{
    public class Filter : IPlugin // Reference this is a plugin
    {
        private IPluginHost _host;

        public string PluginName => "Join-Part Filter";

        public string PluginDescription => "Filters join/part messages.";

        public string PluginAuthor => "Xesyto";    
        public string PluginVersion => "4";      
        public string PluginEmail => "s.oudenaarden@gmail.com";
               
        private readonly Dictionary<string, UserData> _userDatabase;
        private readonly string ColourCode = "8"; //ascii char 3 is included at the start of this string but not clearly visible in VS Editor

        public Filter()
        {
            _userDatabase = new Dictionary<string, UserData>();
        }

        public void Initialize(IPluginHost host)
        {
            // This is called when the plugin is loaded
            // Suscribe to delegates here

            _host = host;

            _host.OnChannelJoin += OnChannelJoin;
            _host.OnChannelPart += OnChannelPart;
            _host.OnQuit += OnQuit;
            _host.OnChannelNormalMessage += OnChannelNormalMessage;
            _host.OnNick += OnNick;
            _host.OnChannelMode += OnChannelMode;
        }

        private void OnChannelNormalMessage(ChannelNormalMessageArgs argument)
        {                        
            //sometimes ( mostly twitch ) user ident or host is not yet known even though a message was received
            if (argument.User == null || argument.User.Ident == null || argument.User.Host == null)
                return;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;
            
            if (!_userDatabase.ContainsKey(userKey))
            {
                var newData = new UserData { AnnouncedJoin = true, LastMessage = DateTime.Now };
                _userDatabase.Add(userKey, newData);
            }
            else
            {
                var userData = _userDatabase[userKey];

                if (!userData.AnnouncedJoin)
                {

                    argument.Message += $" {ColourCode}(logged in {userData.TimeSinceJoin()} ago)";
                    
                    userData.AnnouncedJoin = true;
                }

                userData.LastMessage = DateTime.Now;
            }
        }

        private void OnChannelJoin(ChannelJoinArgs argument)
        {
            argument.EatData = EatData.EatText;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            if (_userDatabase.ContainsKey(userKey))
            {
                var userData = _userDatabase[userKey];
                if (userData.TalkedRecently())
                {
                    argument.EatData = EatData.EatNone;
                }
                userData.Rejoined();
            }
            else
            {
                var userData = new UserData { Joined = DateTime.Now };

                _userDatabase.Add(userKey, userData);
            }
        }

        private void OnChannelPart(ChannelPartArgs argument)
        {
            argument.EatData = EatData.EatText;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            if (!_userDatabase.ContainsKey(userKey)) return;

            var userData = _userDatabase[userKey];
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
                var userKey = argument.Server.Network + channel.Name + argument.User.Host;

                argument.Window.OutputText(userKey);

                if (!_userDatabase.ContainsKey(userKey)) continue;
                
                var userData = _userDatabase[userKey];
                if (userData.TalkedRecently())
                {                    
                    argument.EatData = EatData.EatNone;
                    return;
                }                
            }
        }

        private void OnNick(NickArgs argument)
        {
            argument.EatData = EatData.EatText;
            
            foreach (IChannel channel in argument.Server.GetChannels)
            {
                var userKey = argument.Server.Network + channel.Name + argument.User.Host;

                if (!_userDatabase.ContainsKey(userKey)) continue;
                
                var userData = _userDatabase[userKey];
                if (userData.TalkedRecently())
                {                    
                    argument.EatData = EatData.EatNone;
                    return;
                }
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

            if (!_userDatabase.ContainsKey(userKey)) return;

            var userData = _userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                argument.EatData = EatData.EatNone;
            }
        }
        
        public void Dispose()
        {
        }
    }
}