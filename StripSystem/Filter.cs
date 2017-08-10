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
    //Inerit form IPlugin to be an AdiIRC plugin.
    public class Filter : IPlugin
    {
        private IPluginHost _host;

        //Mandatory information fields.
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
            //Store the host in a private field, we want to be able to access it later
            _host = host;

            //Register Delegates
            _host.OnChannelJoin += OnChannelJoin;
            _host.OnChannelPart += OnChannelPart;
            _host.OnQuit += OnQuit;
            _host.OnChannelNormalMessage += OnChannelNormalMessage;
            _host.OnNick += OnNick;
            _host.OnChannelRawMode += OnChannelRawMode;            
        }

        //Append time to messages
        private void OnChannelNormalMessage(ChannelNormalMessageArgs argument)
        {            
            //sometimes ( mostly twitch ) user ident or host is not yet known even though a message was received
            if (argument.User == null || argument.User.Ident == null || argument.User.Host == null)
                return;
            
            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;
                        
            if (!_userDatabase.ContainsKey(userKey))
            {
                //If the user is new to us add it to the data 
                //We did not see them join so they must have been in channel before us. 
                //So'set AnnounceJoin to true so we don't announce them later.
                var newData = new UserData { AnnouncedJoin = true, LastMessage = DateTime.Now };
                _userDatabase.Add(userKey, newData);
            }
            else
            {
                //if the user was in the database and has not been announced yet, do so.
                var userData = _userDatabase[userKey];

                if (!userData.AnnouncedJoin)
                {                    
                    argument.Message += $" {ColourCode}(logged in {userData.TimeSinceJoin()} ago)";
                    
                    userData.AnnouncedJoin = true;
                }

                //Update last message property.
                userData.LastMessage = DateTime.Now;
            }            
        }

        //Hide join messages
        private void OnChannelJoin(ChannelJoinArgs argument)
        {
            argument.EatData = EatData.EatText;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            if (_userDatabase.ContainsKey(userKey))
            {
                //If the user was already in the data and has talked recently, announce they joined the channel
                var userData = _userDatabase[userKey];
                if (userData.TalkedRecently())
                {
                    argument.EatData = EatData.EatNone;
                }
                userData.Rejoined();
            }
            else
            {
                //If the user is new to us add it to the data 
                var userData = new UserData { Joined = DateTime.Now };

                _userDatabase.Add(userKey, userData);
            }
        }

        //Hide Part Messages
        private void OnChannelPart(ChannelPartArgs argument)
        {
            argument.EatData = EatData.EatText;

            var userKey = argument.Server.Network + argument.Channel.Name + argument.User.Host;

            //if the user is leaving but we don't know about the user yet don't show the PART message and do nothing else
            if (!_userDatabase.ContainsKey(userKey)) return;


            var userData = _userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                //Of they talked recently show the PART message
                argument.EatData = EatData.EatNone;
            }
        }

        //Hide Quite messages
        private void OnQuit(QuitArgs argument)
        {                        
            argument.EatData = EatData.EatText;

            // QUIT messages are server wide not channel specific.
            // But user Identities are stored on a channel basis.
            // So iterate through all channels on the server
            // generating a userkey for them all and checking each individually.

            foreach (IChannel channel in argument.Server.GetChannels)
            {
                var userKey = argument.Server.Network + channel.Name + argument.User.Host;
               
                //This key isn't in the data so skip ahead to the next iteration
                if (!_userDatabase.ContainsKey(userKey)) continue;
                
                var userData = _userDatabase[userKey];
                if (userData.TalkedRecently())
                {
                    //The quitting user talked recently in one of the channels we know about, announce they are quitting.
                    argument.EatData = EatData.EatNone;
                    return;
                }                
            }
        }

        //Hide Nick changes
        private void OnNick(NickArgs argument)
        {            
            argument.EatData = EatData.EatText;
          
            // NICK messages are server wide not channel specific.
            // But user Identities are stored on a channel basis.
            // So iterate through all channels on the server
            // generating a userkey for them all and checking each individually.

            foreach (IChannel channel in argument.Server.GetChannels)
            {
                var userKey = argument.Server.Network + channel.Name + argument.User.Host;

                //This key isn't in the data so skip ahead to the next iteration
                if (!_userDatabase.ContainsKey(userKey)) continue;
                
                var userData = _userDatabase[userKey];
                if (userData.TalkedRecently())
                {
                    //The user talked recently in one of the channels we know about, announce they are changing name.
                    argument.EatData = EatData.EatNone;
                    return;
                }
            }
        }

        //Hide mode changes ( op, voice, hop, etc )
        private void OnChannelRawMode(ChannelRawModeArgs argument)
        {           
            var user = argument.User;
            var mode = argument.Modes;

            //Attempt to find a user ident in case its empty, without it we can't build a userKey
            //This is mostly a twitch issue
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
                    argument.EatData = EatData.EatText;                    
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
                        argument.EatData = EatData.EatText;                        
                        return;
                    }
                }
            }

            argument.EatData = EatData.EatText;
            var userKey = argument.Server.Network + argument.Channel.Name + user.Host;

            if (!_userDatabase.ContainsKey(userKey)) return;

            var userData = _userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                //The user talked recently, announce they are changing mode
                argument.EatData = EatData.EatNone;
            }
            
        }
        
        public void Dispose()
        {
        }
    }
}