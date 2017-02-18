using System;

namespace StripSystem
{
    class UserData
    {
        public DateTime LastMessage;
        public DateTime Joined;

        public bool AnnouncedJoin;

        public bool TalkedRecently()
        {
            var timeDiff = (DateTime.Now - LastMessage).TotalSeconds;

            return !(timeDiff > 600);
        }

        public void Rejoined()
        {
            AnnouncedJoin = false;
            Joined = DateTime.Now;
        }

        public string TimeSinceJoin()
        {
            var timeDiff = (DateTime.Now - Joined);

            if (timeDiff.Days > 1)
                return $"{timeDiff.Days} days, {timeDiff.Hours} hours";

            if(timeDiff.Hours > 1)
                return $"{timeDiff.Hours} hours {timeDiff.Minutes} minutes";

            if (timeDiff.Minutes > 1)
                return $"{timeDiff.Minutes} minutes {timeDiff.Seconds} seconds";

            return $"{timeDiff.Seconds} seconds";
        }
    }
}
