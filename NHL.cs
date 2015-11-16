using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Timers;
using System.Threading;
using Newtonsoft.Json.Linq;
using NLog;
using System.Globalization;



namespace NHLGoalLightSvc
{
    class NHL: IDisposable
    {
        #region VariableDeclaration
        
        public bool GameInProgress = false;
        public DateTime GameDate = DateTime.Now;
        public List<Dictionary<string, string>> GamesToday = new List<Dictionary<string, string>>();

        private static Logger oLog = LogManager.GetCurrentClassLogger();
        private System.Timers.Timer tScoreboard = new System.Timers.Timer();
        private System.Timers.Timer tParticle = new System.Timers.Timer();
        private ParticleAPI oParticle = new ParticleAPI(ParticleAPI.ModeType.All);

        #endregion

        #region Constructors
        /// <summary>
        /// <para>Creates the NHL class and loads the current scoreboard. No timers are initialized.</para>
        /// </summary>
        public NHL()
        {
            init();
        }

        /// <summary>
        /// <para>Creates the NHL class with periodic timers initialized and started for scoreboard refresh and particle updates.</para>
        /// </summary>
        /// <param name="withPeriodicRefresh"></param>
        public NHL(bool withPeriodicRefresh)
        {
            init();
            init_timers();
            start_timers();
        }

        /// <summary>
        /// <para>Creates the NHL class in test mode. No timers are initialized. 
        /// Sends the configured command to the Particle Cloud immediately once. Does not repeat.</para>
        /// </summary>
        /// <param name="IsTestMode"></param>
        /// <param name="sTestMode"></param>
        public NHL(bool IsTestMode, String sTestMode)
        {
            oLog.Debug("NHL Test Mode");

            if (sTestMode == "Goal")
            {
                oParticle.NHLUpdate_Goal("PIT", 1);
                Thread.Sleep(80 * 1000);
                oParticle.NHLUpdate_Goal("PIT", 2);
                Thread.Sleep(80 * 1000);
                oParticle.NHLUpdate_Goal("CBJ", 1);
                Thread.Sleep(80 * 1000); 
            }
            else if (sTestMode == "GameDay")
            {
                oParticle.NHLUpdate_GameDay(new List<string> { "PIT" });
            }
            else if (sTestMode == "GameDay:Goal")
            {
                oParticle.NHLUpdate_GameDay(new List<string> { "PIT:CBJ", "PHI:NJD" });
                Thread.Sleep(20 * 1000);
                oParticle.NHLUpdate_Goal("PIT", 1);
                Thread.Sleep(80 * 1000);
                oParticle.NHLUpdate_Goal("CBJ", 1);
                Thread.Sleep(80 * 1000);
                oParticle.NHLUpdate_Goal("PHI", 1);
                Thread.Sleep(20 * 1000);
                oParticle.NHLUpdate_Goal("PIT", 2);
                Thread.Sleep(80 * 1000);
                oParticle.NHLUpdate_Goal("CBJ", 2);
                Thread.Sleep(80 * 1000);
            }
        }
        #endregion

        #region Initialization
        private void init()
        {
            oLog.Trace("Creating NHL Object");
            // Start by loading game info from NHL website.
            refresh_scoreboard();
        }

        private void init_timers()
        {
            // Setup the scoreboard refresh timer event handler
            tScoreboard.Elapsed += new ElapsedEventHandler(timer_scoreboard_refresh);
            tParticle.Elapsed += new ElapsedEventHandler(timer_particle_refresh);

            // Start a scoreboard refresh timer.
            tScoreboard.Interval = calc_next_scoreboard_refresh_millis();
            tScoreboard.AutoReset = false;
            
            // Start a Particle refresh timer.
            tParticle.Interval = 20 * 1000; // 20 Seconds * 1000 milliseconds. Run the first particle update within a few seconds of service start.
            tParticle.AutoReset = false;
        }

        private void start_timers()
        {
            tScoreboard.Start();
            tParticle.Start();
        }
        #endregion

        #region Private Methods
        private void clear_game_data()
        {
            oLog.Trace("Clearing game data.");
            GamesToday.Clear();
            GameInProgress = false;
        }
        #endregion

        #region Scoreboard Refresh
        private void timer_scoreboard_refresh(object sender, ElapsedEventArgs args)
        {
            oLog.Info("Scoreboard Timer Elapsed. Refreshing scoreboard from external source.");

            // refresh the scoreboard, update the timer interval, start the timer again.
            refresh_scoreboard();
            tScoreboard.Interval = calc_next_scoreboard_refresh_millis();
            oLog.Info("Scoreboard timer reset. Next scoreboard refresh in " + tScoreboard.Interval.ToString() + " milliseconds.");
            tScoreboard.Start();
        }

        private void refresh_scoreboard()
        {
            oLog.Trace("Refreshing Scoreboard.");
            GameDate = calc_gamedate();
            string _sSourceURL = "http://live.nhle.com/GameData/GCScoreboard/";
            string _sResp = "";
            string sURL = _sSourceURL + GameDate.Year.ToString() + "-" + GameDate.ToString("MM") + "-" + GameDate.ToString("dd") + ".jsonp";

            oLog.Debug("Updating NHL scoreboard from external source: " + sURL);

            if (sURL.Contains("nhle.com"))
            {
                // Information regarding nhle.com JSON reponse (URL: http://hfboards.hockeysfuture.com/showthread.php?p=79594853)
                //field                 datatype        descriptipon
                //ata                   char(3)         Away team acronym, 3 letter short name
                //atc                   text            Before: blank During: "progress" After: "winner" if away team wins
                //atcommon              text            away team common name
                //atn                   text            away team city name
                //ats                   int             away team score
                //atsog                 int             Before: null During and After: away team shots on goal
                //bs                    time / text     Before: start time (unsure if local or EST) During: 05:10 2nd, eg After: "FINAL", "FINAL OT" etc
                //bsc                   text            Before: blank During: "progress" After: "final"
                //canationalbroadcasts  text            Canadian TV broadcasters, comma separated list
                //gcl                   Bool            gamecenter live?
                //gcll                  bool            gamecenter live?
                //gs                    int             game status ? 1 = secheduled, 3 = in progress, 5 = finished
                //hta                   char(3)         home team acronym
                //htc                   text            Before: blank During: "progress" After: "winner" if home team wins
                //htcommon              text            home team common name
                //htn                   text            home team city name
                //hts                   int             home team score
                //htsog                 int             Before: null During and After: home team shots on goal
                //id                    int             game ID
                //rl                    Bool            true after game completed
                //usnationalbroadcasts  text            US tv broadcasters, comma separated list

                // create and submit an http request, parse response, load class variables.
                try
                {
                    //oLog.Info("Creating httprequest:  " + sURL);
                    // create a webrequest and receive the response as xml
                    HttpWebRequest oReq = WebRequest.Create(sURL) as HttpWebRequest;
                    HttpWebResponse oResp = oReq.GetResponse() as HttpWebResponse;

                    // load http response into string (this is the JSON payload)
                    using (var reader = new System.IO.StreamReader(oResp.GetResponseStream(), ASCIIEncoding.ASCII))
                    {
                        _sResp = reader.ReadToEnd();
                    }

                    // Fix the response so it starts with {
                    // The NHL response is wrapped in some superfluous characters.
                    _sResp = _sResp.Substring(_sResp.IndexOf("{"));
                    _sResp = _sResp.Substring(0, _sResp.Length - 2);

                    // Create a JSON object and parse JSON.
                    JObject oJSON = JObject.Parse(_sResp);

                    // Compare game date to current date.
                    if (GameDate.Date > DateTime.Parse((string)oJSON["currentDate"]))
                    {
                        oLog.Debug("Clearing game data because NHL current date is before Now(). NHL Current Date: " + (string)oJSON["currentDate"] + " Now: " + GameDate.ToString());
                        // Clear all past game data and load with current data.
                        clear_game_data();
                    }

                    // Setup some local variables to handle the parsing into class variables.
                    JArray aGameIDs = (JArray)oJSON["games"]; // to get a count of # of games.
                    Dictionary<string, string> oDict;

                    GameDate = DateTime.Parse((string)oJSON["currentDate"]); // get the current game date.

                    oLog.Debug("Number of Games today " + GameDate.ToString() + ": " + aGameIDs.Count.ToString());

                    // loop through games in JSON results and load variables.
                    for (int i = 0; i <= (aGameIDs.Count - 1); i++)
                    {
                        bool bExists = false;
                        foreach (Dictionary<string, string> oGames in GamesToday)
                        {
                            if (!oGames.ContainsKey("TeamAbbr"))
                            {
                                oLog.Debug("Dictionary does not contain a team abbreviation. (Breaking...)");
                                break;
                            }
                            else
                            {
                                
                                if (oGames["TeamAbbr"] == (string)oJSON["games"][i]["hta"])
                                {
                                    oLog.Debug("Team record already exists. HomeTeam. (Updating...)");
                                    bExists = true;
                                    oGames["CurrentScore"] = (string)oJSON["games"][i]["hts"];
                                    oGames["ShotsOnGoal"] = (string)oJSON["games"][i]["htsog"];
                                }
                                else if (oGames["TeamAbbr"] == (string)oJSON["games"][i]["ata"])
                                {
                                    oLog.Debug("Team record already exists. AwayTeam. (Updating...)");
                                    bExists = true;
                                    oGames["CurrentScore"] = (string)oJSON["games"][i]["ats"];
                                    oGames["ShotsOnGoal"] = (string)oJSON["games"][i]["atsog"];
                                }
                            }
                        }
                        if (!bExists)
                        {
                            oLog.Debug("Team record does not exist. (Adding...)");
                            DateTime dDate;
                            string sDate = "";
                            string sFormat = "MM/dd/yyyy h:mm tt";

                            // Get Home Team Data...
                            oDict = new Dictionary<string, string>();
                            oDict.Add("TeamAbbr", (string)oJSON["games"][i]["hta"]);
                            oDict.Add("TeamName", (string)oJSON["games"][i]["htcommon"]);
                            oDict.Add("TeamCity", (string)oJSON["games"][i]["htn"]);
                            oDict.Add("Type", "Home");
                            oDict.Add("GameID", (string)oJSON["games"][i]["id"]);
                            oDict.Add("OpponentAbbr", (string)oJSON["games"][i]["ata"]);

                            sDate = GameDate.Date.ToString("MM/dd/yyyy") + " " + (string)oJSON["games"][i]["bs"];
                            if (DateTime.TryParseExact(
                                        sDate
                                        , sFormat
                                        , new CultureInfo("en-US")
                                        , DateTimeStyles.None
                                        , out dDate
                                    )
                               )
                            {
                                oDict.Add("GameTime", (string)oJSON["games"][i]["bs"]);
                                oDict.Add("GameDate", dDate.ToString());
                            }
                            else
                            {
                                oDict.Add("GameStatus", (string)oJSON["games"][i]["bs"]);
                                oDict.Add("GameDate", GameDate.Date.ToString());
                            }


                            oDict.Add("CurrentScore", (string)oJSON["games"][i]["hts"]);
                            oDict.Add("ShotsOnGoal", (string)oJSON["games"][i]["htsog"]);
                            GamesToday.Add(oDict);

                            // Get Away Team Data...
                            oDict = new Dictionary<string, string>();
                            oDict.Add("TeamAbbr", (string)oJSON["games"][i]["ata"]);
                            oDict.Add("TeamName", (string)oJSON["games"][i]["atcommon"]);
                            oDict.Add("TeamCity", (string)oJSON["games"][i]["atn"]);
                            oDict.Add("Type", "Away");
                            oDict.Add("GameID", (string)oJSON["games"][i]["id"]);
                            oDict.Add("OpponentAbbr", (string)oJSON["games"][i]["hta"]);

                            sDate = GameDate.Date.ToString("MM/dd/yyyy") + " " + (string)oJSON["games"][i]["bs"];
                            if (DateTime.TryParseExact(
                                        sDate
                                        , sFormat
                                        , new CultureInfo("en-US")
                                        , DateTimeStyles.None
                                        , out dDate
                                    )
                               )
                            {
                                oDict.Add("GameTime", (string)oJSON["games"][i]["bs"]);
                                oDict.Add("GameDate", dDate.ToString());
                            }
                            else
                            {
                                oDict.Add("GameStatus", (string)oJSON["games"][i]["bs"]);
                                oDict.Add("GameDate", GameDate.Date.ToString());
                            }


                            oDict.Add("CurrentScore", (string)oJSON["games"][i]["ats"]);
                            oDict.Add("ShotsOnGoal", (string)oJSON["games"][i]["atsog"]);
                            GamesToday.Add(oDict);
                        }
                    }

                    // for testing, write games to console
                    //foreach (Dictionary<string, string> oGames in GamesToday)
                    //{
                    //    Console.WriteLine(
                    //        "Team: " + oGames["TeamAbbr"] +
                    //        "; Time: " + oGames["GameTime"] +
                    //        "; Opponent: " + oGames["OpponentAbbr"] +
                    //        "; Score: " + oGames["CurrentScore"] +
                    //        "; SoG: " + oGames["ShotsOnGoal"]);

                    //}

                }
                catch (Exception e)
                {
                    oLog.Error("Error: " + e.Message + Environment.NewLine);
                    oLog.Error("Submitted URL: " + sURL + Environment.NewLine);
                    oLog.Error("Stack Trace: " + e.StackTrace + Environment.NewLine);
                }
            }

            set_current_scores();
            oLog.Trace("Scoreboard Refresh Complete.");
        }

        private DateTime calc_gamedate()
        {
            if (GameInProgress)
            {
                return GameDate;
            }
            else
            {
                return DateTime.Now;
            }
        }

        private void set_current_scores()
        {
            oLog.Trace("Posting current scores.");

            try
            {
                foreach (Dictionary<string, string> oDict in GamesToday)
                {
                    if (oDict.ContainsKey("CurrentScore"))
                    {
                        if (oDict["CurrentScore"] == "") { oDict["CurrentScore"] = 0.ToString(); }
                    }
                    else
                    {
                        oLog.Debug("Current Score missing for team " + oDict["TeamAbbr"] + ".");
                    }
                        
                    if (oDict.ContainsKey("PriorScore"))
                    {
                        oLog.Debug("Prior score exists. (Updateing... Team: " + oDict["TeamAbbr"] + "  Current Score: " + oDict["CurrentScore"] + "  Prior Score: " + oDict["PriorScore"] + ")");
                        
                        if (int.Parse(oDict["CurrentScore"]) > int.Parse(oDict["PriorScore"]))
                        {
                            // log the goal event
                            oLog.Info("GOAL!: " + oDict["TeamAbbr"] + ";" + oDict["CurrentScore"]);

                            oParticle.NHLUpdate_Goal(oDict["TeamAbbr"], int.Parse(oDict["CurrentScore"]));
                            oDict["PriorScore"] = oDict["CurrentScore"];
                        }
                    }
                    else
                    {
                        oDict.Add("PriorScore", oDict["CurrentScore"]);
                        oLog.Trace("No prior score for: " + oDict["TeamAbbr"] + " (adding...)");
                    }
                }
            }
            catch (Exception e)
            {
                oLog.Error("Posting scores failed.");
                oLog.Error("Error: " + e.Message);
                oLog.Error("Stack Trace: " + e.StackTrace.ToString());
            }

            oLog.Trace("Posting scores complete.");

        }

        private double calc_next_scoreboard_refresh_millis()
        {
            // Returns the number of milliseconds until the next required refresh.
            // Starting 5 minutes before published gametime, we refresh every 1 minute.
            // Other wise, we refresh every 1 hour.
            // If any game starts within the hour, return number of milliseconds
            //   until 5-minutes prior to game start.

            oLog.Trace("Calculating next refresh millis.");

            if (GamesToday.Count > 0)
            {
                DateTime dNow = DateTime.Now;
                DateTime dNextGame = DateTime.Parse("1/1/2045 08:00 AM");
                DateTime dTemp = DateTime.Parse("1/1/2045 08:00 AM");
                bool bInProgress = false;

                oLog.Debug("DateTime variables created:");
                oLog.Debug("Time Variables:  Now: " + dNow.ToString() + "  NextGame: " + dNextGame.ToString() + "  Temp: " + dTemp.ToString());
                // Conditions to return 1 minute:
                //   Any game is "in progress".
                //   Any game starts within 5 minutes.

                // Conditions to return less than 1 hour:
                //   Any game start time (minus 5 minutes), starts within 1 hour.

                // Else return 1 hour (60 minutes * 60 seconds * 1000 milliseconds).

                // Parse the GamesToday list and find:
                //   Any games "in progress".
                //   Any game start times (minus 5 minutes), that start within 1 hour.
                try
                {
                    foreach (Dictionary<string, string> oDict in GamesToday)
                    {
                        // If there is a current GameStatus, then game is in progress or a final.
                        if (oDict.ContainsKey("GameStatus") && !bInProgress)
                        {
                            if (!oDict["GameStatus"].Contains("FINAL"))
                            {
                                bInProgress = true;
                                oLog.Debug("There is a game in progress: " + oDict["TeamAbbr"]);
                            }
                        }

                        // If there is a gamedate and time, compare it to now and
                        // if it is the next game to start.
                        dTemp = DateTime.Parse("1/1/2045 08:00 AM");
                        if (oDict.ContainsKey("GameDate") && oDict.ContainsKey("GameTime") && !bInProgress)
                        {
                            oLog.Debug("Calculating gamedate from: " + oDict["GameDate"]);
                            dTemp = DateTime.Parse(oDict["GameDate"]);
                            oLog.Debug("Is NextGame compareto dTemp? " + dNextGame.CompareTo(dTemp).ToString() + "    Is Now compareto dTemp? " + dNow.CompareTo(dTemp).ToString());

                            // NextGame should be later than now. but now should be earlier than the temp.
                            if (dNextGame.CompareTo(dTemp) > 0 && dNow.CompareTo(dTemp) < 0)
                            {
                                oLog.Debug("Updating NextGame to: " + dTemp.ToString());
                                dNextGame = dTemp;
                            }
                        }
                    }

                    // if there is a game in progress, return 60 seconds.
                    if (bInProgress)
                    {
                        GameInProgress = true;
                        oLog.Debug("Returning 1 minute. Game in progress.");
                        return 60 * 1000; // 60 seconds * 1000 milliseconds
                    }
                    else
                    {
                        GameInProgress = false;
                        oLog.Debug("Time Variables:  Now: " + dNow.ToString() + "  NextGame: " + dNextGame.ToString() + "  Temp: " + dTemp.ToString());
                        oLog.Debug("No games in progress. Next Game in " + dNextGame.Subtract(dNow).TotalMinutes.ToString() + " minutes.");
                        if (dNextGame.Subtract(dNow).TotalMinutes >= 65)
                        {
                            oLog.Debug("Returning 60 minutes. No games starting in the next 65 minutes.");
                            return 60 * 60 * 1000; // 60 minutes * 60 seconds * 1000 milliseconds
                        }
                        else
                        {
                            oLog.Debug("Returning " + dNextGame.Subtract(new TimeSpan(0, 5, 0)).Subtract(dNow).TotalMinutes.ToString() + " minutes. Game starting soon.");
                            return dNextGame.Subtract(new TimeSpan(0, 5, 0)).Subtract(dNow).TotalMilliseconds;
                        }
                    }
                }
                catch (Exception e)
                {
                    //oEventLog.WriteEntry("NHL Calc Next Refresh Millis Error (Error: " + e.Message + ")", EventLogEntryType.Error);
                    oLog.Error("NHL Calc Next Refresh Millis Error");
                    oLog.Error("Error: " + e.Message);
                    oLog.Error("Stack Trace: " + e.StackTrace.ToString());

                    oLog.Error("Returning 10 minutes. Error calculating next refresh.");
                    return 10 * 60 * 1000; // 10 minutes * 60 seconds * 1000 milliseconds
                }
            }
            else
            {
                clear_game_data();
                oLog.Debug("Returning 60 minutes. No Games Today.");
                return 60 * 60 * 1000; // 60 minutes * 60 seconds * 1000 milliseconds
            }
        }
        #endregion

        #region Particle Refresh
        private void timer_particle_refresh(object sender, ElapsedEventArgs args)
        {
            oLog.Info("Particle Refresh Timer Elapsed. Posting feeds to Particle.");

            // Run the NHL updates
            PostUpdatesToParticle();

            // Set new timer interval
            tParticle.Interval = calc_next_particle_refresh_millis();

            // Log it.
            oLog.Trace("Posting to Particle complete. Next posting in " + tParticle.Interval.ToString() + " milliseconds");

            // Start the time again.
            tParticle.Start();
        }

        public void PostUpdatesToParticle()
        {
            oLog.Trace("Starting Post Updates");

            // post gameday feeds to particle stream
            List<string> oTeams = new List<string>();
            foreach (Dictionary<string, string> oDict in GamesToday)
            {
                if (oDict.ContainsKey("TeamAbbr") && oDict.ContainsKey("Type"))
                {
                    // Only add team pairs when the team is a home team, and doesn't already exists in the list.
                    if (!string.Join(";", oTeams).Contains(oDict["TeamAbbr"]) && oDict["Type"] == "Home")
                    {
                        // For each home team, find the opponent.
                        foreach (Dictionary<string, string> oDict2 in GamesToday)
                        {
                            if (oDict2.ContainsKey("OpponentAbbr"))
                            {
                                //oLog.Debug("... searching for opponent: " + oDict2["OpponentAbbr"]);
                                if (oDict["TeamAbbr"] == oDict2["OpponentAbbr"])
                                {
                                    oLog.Debug("Found opponent: " + oDict["TeamAbbr"]);
                                    oTeams.Add(oDict["TeamAbbr"] + ":" + oDict2["TeamAbbr"]);
                                }
                            }
                        }
                    }
                }
            }
            oLog.Debug("Posting gameday for these teams: " + string.Join(";", oTeams));
            oParticle.NHLUpdate_GameDay(oTeams);
            oLog.Trace("Post updates completed.");

            // post games in progress
            //string sInProgress = "InProgress";
            //string sFinal = "Final";
            //List<string> lInProgress = new List<string>();
            //List<string> lFinal = new List<string>();
            //foreach (Dictionary<string, string> oDict in GamesToday)
            //{
            //    if (oDict.ContainsKey("GameDate") && oDict.ContainsKey("GameTime"))
            //    {
            //        if (oDict.ContainsKey("GameStatus"))
            //        {
            //            if (
            //                oDict["GameStatus"].Contains("1st") ||
            //                oDict["GameStatus"].Contains("2nd") ||
            //                oDict["GameStatus"].Contains("3rd")
            //            )
            //            {
            //                sInProgress = sInProgress + ";" + oDict["TeamAbbr"];
            //                lInProgress.Add(oDict["TeamAbbr"]);
            //            }
            //            else if (oDict["GameStatus"].Contains("final"))
            //            {
            //                sFinal = sFinal + ";" + oDict["TeamAbbr"];
            //                lFinal.Add(oDict["TeamAbbr"]);
            //            }
            //        }
            //    }
            //}
            //oParticle.NHLUpdate_GameStatus()
        }

        private double calc_next_particle_refresh_millis()
        {
            if (GameInProgress)
            {
                return 60 * 1000; // 60 seconds * 1000 milliseconds
            }
            else
            {
                return 10 * 60 * 1000; // 10 minutes * 60 seconds * 1000 milliseconds
            }
        }
        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                oLog.Trace("Disposing NHL Object.");
                tScoreboard.Stop();
                tScoreboard.Dispose();
                tParticle.Stop();
                tParticle.Dispose();
                clear_game_data();

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~NHL() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
