using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AdiIRCAPI; // Include the API

namespace StripSystem
{
    public class Filter : IPlugin // Reference this is a plugin
    {
        IPluginHost _myHost = null;
        ITools _myTools = null;

        public string Description => "Filters join/part messages.";

        public string Author => "Xesyto";

        public string Name => "Join-Part Filter";

        public string Version => "3";

        public string Email => "";

        public IPluginHost Host
        {
            get { return _myHost; }
            set { _myHost = value; }
        }

        public ITools Tools
        {
            get { return _myTools; }
            set { _myTools = value; }
        }

        private Dictionary<string, UserData> userDatabase;
        private string ColourCode = "8"; //ascii char 3 is included at the start of this string but not clearly visible in VS Editor

        public Filter()
        {
            userDatabase = new Dictionary<string, UserData>();
        }

        public void Initialize()
        {
            // This is called when the plugin is loaded
            // Suscribe to delegates here
            _myHost.OnJoin += myHost_OnJoin;
            _myHost.OnPart += myHost_OnPart;
            _myHost.OnQuit += myHost_OnQuit;            
            _myHost.OnMessage += myHost_OnMessage;
            _myHost.OnNick += myHost_OnNick;
            _myHost.OnMode += myHost_OnMode;
        }

        private void myHost_OnMode(IServer server, IChannel channel, IUser user, string mode, out EatData Return)
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
                    Return = EatData.EatNone;
                    return;
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

        private void myHost_OnNick(IServer server, IUser user, string newNick, out EatData Return)
        {
            Return = EatData.EatText;

            foreach (IChannel channel in server.GetChannels)
            {
                var userKey = server.Network + channel.Name + user.Ident + user.Host;

                if (!userDatabase.ContainsKey(userKey)) continue;

                var userData = userDatabase[userKey];
                if (!userData.TalkedRecently()) continue;

                Return = EatData.EatNone;
                return;
            }
        }

        void myHost_OnQuit(IServer server, IUser user, string data, out EatData Return)
        {
            Return = EatData.EatText;

            foreach (IChannel channel in server.GetChannels)
            {
                var userKey = server.Network + channel.Name + user.Ident + user.Host;

                if (!userDatabase.ContainsKey(userKey)) continue;

                var userData = userDatabase[userKey];
                if (!userData.TalkedRecently()) continue;

                Return = EatData.EatNone;
                return;
            }
        }

        void myHost_OnPart(IServer server, IChannel channel, IUser user, string partMessage, out EatData Return)
        {
            Return = EatData.EatText;
            var userKey = server.Network + channel.Name + user.Ident + user.Host;

            if (!userDatabase.ContainsKey(userKey)) return;

            var userData = userDatabase[userKey];
            if (userData.TalkedRecently())
            {
                Return = EatData.EatNone;
            }            
        }

        void myHost_OnJoin(IServer server, IChannel channel, IUser user, out EatData Return)
        {
            Return = EatData.EatText;

            var userKey = server.Network + channel.Name + user.Ident + user.Host;

            if (userDatabase.ContainsKey(userKey))
            {
                var userData = userDatabase[userKey];
                if (userData.TalkedRecently())
                {
                    Return = EatData.EatNone;
                }
                userData.Rejoined();                
            }
            else
            {
                var userData = new UserData {Joined = DateTime.Now};

                userDatabase.Add(userKey,userData);
            }
        }

        void myHost_OnMessage(IServer server, IChannel channel, IUser user, string message, out EatData Return)
        {            
            Return = EatData.EatNone;

            //sometimes ( mostly twitch ) user ident or host is not yet known even though a message was received
            if (user == null || user.Ident == null || user.Host == null)
                return;

            var userKey = server.Network + channel.Name + user.Ident + user.Host;

            if (!userDatabase.ContainsKey(userKey))
            {                
                var newData = new UserData {AnnouncedJoin = true,LastMessage = DateTime.Now};
                userDatabase.Add(userKey,newData);
            }
            else
            {
                var userData = userDatabase[userKey];

                if (!userData.AnnouncedJoin)
                {
                    Return = EatData.EatAll;

                    var newMessage = $":{user.Nick}!{user.Ident}@{user.Host} PRIVMSG {channel.Name} :{message} {ColourCode}(logged in {userData.TimeSinceJoin()} ago)";
                    server.SendFakeRaw(newMessage);

                    userData.AnnouncedJoin = true;
                }

                userData.LastMessage = DateTime.Now;
            }
        }

        public void Dispose()
        {
            // This is called when the plugin is unloaded
        }
    }
}