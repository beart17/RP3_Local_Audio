using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Timers;
using NAudio.Wave;
using SoundTouch;
using std;
using System.IO;
using System.Reflection;
using SoundTouch.Net.NAudioSupport;


/*
 * Distance over time is the performance output of the C2
 * 
 * 
 * Remarks:
 * Neos input format : {C/F,time,} 
 * to add?: strokelength current?
 * 
 * assumptions:
 * Recovery -> Assume linear deceleration
 * Drag force proportional to (rotational) velocity^2
 * 
 * to fix:
 * calculate first angular displacement into virtual boat speed, then later on make it into speed
 * or calculate from here the angular velocity?
 * Distinguish between instant and linear values..., create instantVelocity etc.
 * 
 * 
 * linear distance; the estimated distance boat is expected to travel. Do we need the total angular displacement from start?
 * Linear velocity; the speed at which the boat is expected to travel
 * 
 * instantAngularVelocity; instant calculation of angular velocity, prone to errors.
 * To avoid data spikes, a running average of currentDt is run
 * OpenRowing uses average of 3-> more accurate/responsive; 6-> more smooth for recreational rowing.
 * 
 * 
 * for later:
 * RP3 flywheel weight: 21.5lkg
 * 
 * 
 * Fixed df of 120 (indicates heavy rowing)
 * Moment of cycle indication
 * 
 */

namespace RP3_Interface
{
    
    public class Rower
    {
        //State tracking
        public enum State { Idle, Drive, Recovery };
        private State currState;
        private Drive drive;
        private Recovery recovery;
        private bool isDriveStarting;
        private double predictedDriveStartTime;

        //dragfactor fixed (or dynamic)
        private bool resistanceHigh;
        private bool fixedValue;

        //running average
        public int n_runningAvg = 3;
        private Queue<double> AverageQueue;

        //RP3 input
        public float currentDt;
        const int nImpulse = 4;
        int totalImpulse;
        double totalTime;

        //constants
        const float angularDis = (2 * (float)Math.PI) / nImpulse;
        const float inertia = 0.1001f;
        const float magicFactor = 2.8f;

        //angular values
        public float currTheta, currW;
        public float dragFactor;
        private float conversionFactor;

        //timers
        System.Timers.Timer timer;
        Stopwatch strokeTimer;
        //Timer driveTimer;
        Queue<double> lastStrokeTimes;
        Timer CvsTimer;

        //sound
        int strokeCount;
        private int strokeCounter;
        private SoundTouchProcessor soundTouch;
        //private System.Media.SoundPlayer player;
        string audioFilePath = "C:\\Users\\bartb\\OneDrive - University of Twente\\Documenten\\University\\Module 11\\GP - Rowing Reimagined\\B4.wav";
        List<double> strokeTimes = new List<double>();
        double desiredDelay = 0.0; //Delay in sec before playing the audio fragment
        private double soundDelay = 0.0;
        double speedAdjustment;
        //private AudioFileReader audioFile;

        double currDriveTime;

        //CVS File 
        private string fileName;
        private string average;
        private bool shouldWriteCSV;
        private bool checkHeader;

        public Rower()
        {
            //state idle on start? or use incoming message to do?
            this.currState = State.Drive;
            this.drive = new Drive();
            this.recovery = new Recovery();
            isDriveStarting = false;
            resistanceHigh = false;
            fixedValue = true;
            shouldWriteCSV = false;
            checkHeader = true;
            predictedDriveStartTime = 0.0;
            totalImpulse = 0;
            totalTime = 0;
            strokeCount = 0;
            strokeCounter = 1;
            strokeTimes = new List<double>();
            AverageQueue = new Queue<double>(n_runningAvg);

            if (this.resistanceHigh) this.dragFactor = 130; // between 100 and 125, 1e-6 is accounted for
            else this.dragFactor = 120;

            timer = new System.Timers.Timer();
            timer.Interval = 3000;
            SetTimer(timer);

            this.strokeTimer = new Stopwatch();
            lastStrokeTimes = new Queue<double>();

            soundTouch = new SoundTouchProcessor();
            //audioFile = new AudioFileReader(audioFilePath);
            //player = new System.Media.SoundPlayer(audioFilePath); // create new instance sound player class with audio file path
            //player.LoadAsync(); // load in audio 

            CvsTimer = new Timer();
            CvsTimer.Interval = 1000; // Set the interval to 1 second (adjust as needed)
            CvsTimer.Elapsed += OnCvsTimerElapsed; // Hook up the Elapsed event
            CvsTimer.AutoReset = true; // Set AutoReset to true to repeat the timer
            CvsTimer.Enabled = false; // Initially disable the timer

            start(); // call the Start method to initialize the filename variable

            reset(); // set initial values
        }


        public void onImpulse(double currentDt)
        {
            //RP3 input
            this.currentDt = (float)RunningAverage(currentDt);
            totalImpulse++;
            totalTime += currentDt;

            //angular values
            this.currTheta = angularDis * totalImpulse;

            float tempW = this.currW;
            this.currW = angularDis / this.currentDt;
            float DeltaW = this.currW - tempW;

            //check if we need to switch states
            onStateSwitch(DeltaW, this.currentDt);

            //send back data to Neos
            this.SendDataFormat();
        }
        
        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            Console.WriteLine("Timer triggered {0:HH:mm:ss.fff}",e.SignalTime);

        }

        public double RunningAverage(double dt)
        {
            if (AverageQueue.Count == n_runningAvg)
            {
                AverageQueue.Dequeue(); //remove last dt, does not auto do it
            }
            AverageQueue.Enqueue(dt); //add to queue

            return AverageQueue.Sum() / n_runningAvg;
        }

        private float CalculateCurrentTheta(int totalImpulse)
        {
            const float angularDis = (2 * (float)Math.PI) / nImpulse;
            return angularDis * totalImpulse;
        }

        private float CalculateCurrentW(float currTheta, double currentDt)
        {
            return (float)(currTheta / currentDt);
        }

        private void SetTimer(System.Timers.Timer timer)
        {
            // Hook up the Elapsed event for the timer. 
            timer.Elapsed += OnTimedEvent;
            timer.AutoReset = true;
            timer.Enabled = true;
        }

       /* private void calcStrokeTime()
        {
            if (strokeCount > 0)
            {
                double elapsedSeconds = strokeTimer.Elapsed.TotalSeconds;
                strokeTimes.Add(elapsedSeconds);
                Console.WriteLine("Stroke Time: " + elapsedSeconds);
                strokeTimer.Restart();
            }

            strokeCount++;

            if (strokeCount == 3)
            {
                double averageStrokeTime = strokeTimes.Average();
                Console.WriteLine("Average Stroke Time: " + averageStrokeTime);
                //ModifyAudioFragment(averageStrokeTime);
            }
        }*/

/*        private double GetCurrentTime()
        {
            return strokeTimer.Elapsed.TotalSeconds;
        }*/

        private void ModifyAudioFragment()
        {

            AudioFileReader audioFile = new AudioFileReader(audioFilePath);

            double averageStrokeTime = lastStrokeTimes.Any() ? lastStrokeTimes.Average() : 0.0;
            double adjustedTime = averageStrokeTime / 3.0f; // Increase target time by 0.1 seconds

            if (averageStrokeTime > 3 && averageStrokeTime < 5) 
            {
                // Calculate the desired speed adjustment factor
                speedAdjustment = adjustedTime + 2.1; // averageStrokeTime;
            }

            if (averageStrokeTime < 3 && averageStrokeTime > 1)
            {
                // Calculate the desired speed adjustment factor
                speedAdjustment = adjustedTime + 2.0; // averageStrokeTime;
               
            }

            double inclinedTime = speedAdjustment * 3.0f;

            // Apply the speed adjustment using SoundTouch library
            soundTouch.TempoChange = speedAdjustment;
            Console.WriteLine("Adjusted time " + inclinedTime);
            Console.WriteLine("SpeedAdjustment factor " + speedAdjustment);

            // Create a SoundTouchWaveProvider with the modified audio
            var soundTouchWaveProvider = new SoundTouchWaveProvider(audioFile, soundTouch);

            // Create a WaveOutEvent to play the modified audio
            var waveOut = new WaveOutEvent();
            waveOut.Init(soundTouchWaveProvider);
            waveOut.Play();
            SoundTouchProcessor
        }
        
    //triggers when socket receives message
    public void onStateSwitch(float dw, float dt)
        {
            //timer.start()
            //stroketimer.restart()
            //timer.stop()

            float currAccl = dw / dt;

            //Console.Write("Acceleration" + currAccl);

            if (currAccl >= 0.2f && currState == State.Recovery) //switch to drive
            {

                if (strokeTimer.IsRunning)
                {
                    OnStrokeStart();
                    strokeTimer.Restart();

                    strokeCounter++;
                }
                else
                {
                    strokeTimer.Start();
                }

                ModifyAudioFragment(); //Play audio fragment

                this.currState = State.Drive;
                EndOfState(inertia, currTheta, dw);
            }
            else if (currAccl <= -0.1f && currState == State.Drive)//switch to recovery
            {
                currDriveTime = strokeTimer.Elapsed.TotalSeconds;
                Console.WriteLine(" ");
                Console.WriteLine("Drive time : " + currDriveTime);
                this.currState = State.Recovery;
                EndOfState(inertia, currTheta, dw);

                //todo: when idle
                //this.currState = State.Idle;
            }
        }

        private void EndOfState(float I, float t, float w)
        {
            
            Console.WriteLine("End of: " + this.currState);

            switch (this.currState)
            {
                case State.Drive:
                    this.drive.setEnd(t, w);
                    this.recovery.setStart(t, w);
                    this.drive.linearCalc(conversionFactor, t, w);
                    this.drive.reset();

                    break;
                case State.Recovery:
                    this.recovery.setEnd(t, w);
                    this.drive.setStart(t, w);


                    //this.dragFactor = this.recovery.calcDF(I, time, this.dragFactor, this.fixedValue);
                    this.conversionFactor = updateConversionFactor();

                    this.recovery.linearCalc(conversionFactor, t, w);
                    this.recovery.reset();

                    break;
                    /*case State.Idle:
                        this.drive.reset();
                        this.recovery.reset();
                        this.reset();
                        break;*/
            }
        }

        private float[] convert(string data)
        {
            CultureInfo invC = CultureInfo.InvariantCulture;
            string[] d = data.Split(',');
            string[] values = d.Skip(1).ToArray(); //skip first element, create new array
            var parsedValues = Array.ConvertAll(values, float.Parse);
            float.TryParse(values[0], NumberStyles.Number, invC, out parsedValues[0]);

            return parsedValues;
        }

        private float updateConversionFactor()
        {
            return (float)Math.Pow(((Math.Abs(dragFactor)/1000000) / magicFactor), 1f / 3f);
        }

        public string SendDataFormat()
        {
            if (currState == State.Drive) return $"{currentDt}/{currW}/{drive.linearVel}\n";
            else return $"{currentDt}/{currW}/{recovery.linearVel}\n";
        }
        
        public void reset()
        {
            //initial values
            drive.reset();
            recovery.reset();
            totalImpulse = 0;
            totalTime = 0;
            strokeCount = 0;
            strokeTimes.Clear();

            conversionFactor = (float)(2 * Math.PI * magicFactor);
        }

        public void shutdown()
        {
            timer.Stop();
            timer.Dispose();
        }

       public void OnStrokeStart()
        {
            double strokeTime = strokeTimer.Elapsed.TotalSeconds;
            lastStrokeTimes.Enqueue(strokeTime);
            Console.WriteLine("Total stroke time " + strokeTime);

            if (lastStrokeTimes.Count > 5) lastStrokeTimes.Dequeue();

            double average = lastStrokeTimes.Average();
            Console.WriteLine("Average stroke time " + average);
            //Console.WriteLine("Recovery time " + currDriveTime);

            WriteCSV();
        }

        void start()
        {
            string directoryPath = @"C:\Users\bartb\OneDrive - University of Twente\Documenten\University\Module 11\GP - Rowing Reimagined\Data"; // Specify the desired directory path
            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss"); // Add a timestamp to the file name
            fileName = Path.Combine(directoryPath, $"RowingData_{timeStamp}.csv"); // Combine the directory path and timestamped file name
        }

        private void OnCvsTimerElapsed(object sender, ElapsedEventArgs e)
        {
            WriteCSV();
        }

        public void WriteCSV()
        {
            using (TextWriter tw = new StreamWriter(fileName, true))
            {
                if (checkHeader)
                {
                    tw.WriteLine("stroke count; currentDt; currW; linearVel; Total stroke time; Average stroke time; Drive time; Recovery time");
                    checkHeader = false;
                }

                double totalStrokeTime = strokeTimer.Elapsed.TotalSeconds;
                double recoveryTime = totalStrokeTime - currDriveTime;
                double averageStrokeTime = lastStrokeTimes.Any() ? lastStrokeTimes.Average() : 0.0;

                if (currState == State.Drive)
                {
                    tw.WriteLine($"{strokeCounter};{currentDt};{currW};{drive.linearVel};{totalStrokeTime};{averageStrokeTime};{currDriveTime};{recoveryTime}");
                }
                else
                {
                    tw.WriteLine($"{strokeCounter};{currentDt};{currW};{recovery.linearVel};{totalStrokeTime};{averageStrokeTime};{currDriveTime};{recoveryTime}");
                }
            }

            //Console.WriteLine("Your CSV was saved!");
        }
    }
}
