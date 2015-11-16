# ParticleSportsUpdaterService
A Windows C# .NET service that polls sports websites and updates particle (particle.io) devices with sports scores.

Project Origins:
I wanted to create an NHL Goal Light that would turn on automatically every time my favorite NHL team scores a goal. Google the "BudweiserGoal Light" for an example. I want a simple device that you "set and forget"; unlike the Budweiser lights, I do not want to connect the light to my smartphone... my light will be autonomous. I chose the Particle Photon as my hardware development board due to the low cost and built-in WIFI. The online Particle IDE was intriguing as well. As I researched how to get game scores on the photon, I realized it was probably easier to write a windows service in .NET that would periodically parse the NHL website for game data and push scores/events to the Particle Core via the Particle Cloud. 

Basic Functionality:
When the service is started, it instantiates a class for each sport league. The league classes poll their respective websites for daily game schedules and current game scores. The polling interval is typically long (1 hour or more) when a game is not in progress. The polling interval shortens to 1 minute or less when a game is in progress. There is a separate polling interval for when the class pushes information to the particle cloud. Typically, the GameDay function is called every 10 minutes when no game is in progress and then every 1 minute when a game is in progress. The particle cloud only allows 4 functions to be registered, therefore, all commands are wrapped into a function called "GameUpdate". GameUpdate takes the following command arguments:  GameDay, GameStatus, Score (or Goal). 

Prerequisites:
1. Reference to JSON.net (by Newtonsoft) - available with NuGet
2. Reference to SparkIO.Webservices (by chrisloris) - available with NuGet (and available on GitHub @ https://github.com/chrisloris/SparkIODotNet/tree/master/SparkIODotNet%20Solution/WebServices)

Classes:
NHL - Does the parsing of the nhle.com JSON response at periodic intervals. Contains 2 timers, one for updating the scores from nhle.com and the other for pushing the info to the particle cloud. There are no user definable variables in this class. There are 3 constructors which allow you to use to instantiate the class without timers, with timers, and in test mode.

Particle API - Does validation for particle device connectivity before calling the particle cloud functions. The ParticleAccessToken applicaton variable needs set in order to access the user's Particle cloud account. There are 3 modes available: Single (only one device is notified), Multi (multiple devices are notified), and All (all devices available on the user's Particle account are notified.) In Single and Multi mode, the DeviceIDList must be maintained by the user either programatically or by using the ParticleDeviceList application variable. In All mode, only the AccessToken needs to be defined in the application variables.

Test Mode (NHL):
You can put the service in test mode by setting the TestMode application variable to Goal, GameDay, or Goal:GameDay. In test mode, the service sends out a sequence of commands to the particle cloud for testing the goal light. You can set the command sequence by editing the NHL class constructor for test mode. 

Application Variables:
Test Mode (string) - Set to one of (Goal, GameDay, or Goal:GameDay) if you want to send specific commands to the particle device for testing purposes.
ParticleAccessToken (string) - The access token for your Particle.io account. Log into particle.io, click on "Build" then click on "Settings". Copy and paste your access token.
ParticleDeviceList (string) - This is a comma delimited list of particle device IDs you want to notify. This variable is only used in "Single" and "Multi" Particle API modes.
Interval_GameDay (integer) - Set the interval (in minutes) to poll for schedules on a Game Day. (Typicall is 60 minutes.)
Interval_GameInProgress (integer) - Set the interval (in minutes) to poll for game scores when a game is in progress. (Typically 1 minute.)
Interval_ParticlePush (integer) - Set the interval (in minutes) to push particle updates. When a game is in progress, updates are pushed using Interval_GameInProgress. NOTE: When you first turn on a particle device, and a game is not in progress, this is the maximum amount of time before the particle receives an update.

Notes:
1. Ideally, if I can code it in the Particle IDE, I will parse the league websites on the photon directly. However, in lieu of scrapping this code, I may transform this code into a hosted web application that will return a JSON response tailored to the application. That way, I can parse a much simpler set of responses that I control. The advantage of the service is that it can run behind a firewall on any computer that is contantly on. The disadvantage of a hosted web app is finding a .NET host.

