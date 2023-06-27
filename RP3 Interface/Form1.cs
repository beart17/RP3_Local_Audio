using Fleck;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;


namespace RP3_Interface
{
    public partial class Form1 : Form
    {
        public static bool formOpen = true;
        string lastMessage = "";

        StringBuilder log = new StringBuilder();

        double lastRps = 0;
        double change = 0;
        WebSocketServer wss;
        List<IWebSocketConnection> allSockets = new List<IWebSocketConnection>();

        //Create rower instance for physics calculations
        Rower rower = new Rower();


        public Form1()
        {
            CheckForIllegalCrossThreadCalls = false;
            InitializeComponent();
            Console.WriteLine("hello world");

            Timer updateClock = new Timer();
            //updateClock.Interval = 1000;
            updateClock.Interval = 1000 / 60;
            updateClock.Tick += Update;
            updateClock.Start();

            this.FormClosing += formClosing;
            serialPort1.DataReceived += dataReceived;

            wss = new WebSocketServer("ws://127.0.0.1:2070");
            wss.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    allSockets.Add(socket);
                };
                socket.OnClose = () => { allSockets.Remove(socket); };
                //socket.OnMessage += OnMessage;
            });



            //ensure its empty
            log.Clear();
        }

        void formClosing(object sender, EventArgs e)
        {
            wss.Dispose();
            serialPort1.Close();
            formOpen = false;
        }

        private void FindMachine_Click(object sender, EventArgs e)
        {
            string selectedName = "";
            if (PortsList.CheckedItems.Count > 0)
                selectedName = (string)PortsList.CheckedItems[0];

            //add all port names to PortsList as selectable checkboxes
            PortsList.Items.Clear();
            string[] names = SerialPort.GetPortNames();
            foreach (string s in names)
            {
                int i = PortsList.Items.Add(s);

                if (s.Equals(selectedName))
                {
                    Console.WriteLine("setting index to " + i);
                    PortsList.SetItemChecked(i, true);
                }
            }
        }

        private void PortsList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
                serialPort1.Close();
            if (PortsList.CheckedItems.Count > 0)
            {
                serialPort1.PortName = (string)PortsList.CheckedItems[0];
                serialPort1.Open();
                Console.WriteLine("current open port: " + serialPort1.PortName + ": " + serialPort1.IsOpen);
            }
        }

        private void dataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!serialPort1.IsOpen || serialPort1.BytesToRead < 2)
                return;
            //Console.WriteLine("Serialport bytes to read: "+ serialPort1.BytesToRead);

            byte[] input = new byte[serialPort1.BytesToRead];
            int valueCount = serialPort1.Read(input, 0, input.Length);

            int i = 0;
            while (i < valueCount && i < input.Length)
            {
                int value = ((input[i] & 0xff) << 8 | (input[i + 1] & 0xff)) & 0xffff;
                double wheelValue = value / 750000.0; //the deltatime between impulses
                double rps = 1 / (wheelValue * 4); //rotation per second, if deltatime is same throughout one rotation

                //provide rower
                this.rower.onImpulse(wheelValue);

                //chart
                //addPoint(1, change, 500, true, true);


                /*
                chart1.Series[0]?.Points.AddY(rps);
                if (chart1.Series[0]?.Points.Count > 500)
                {
                    //remove first index
                    chart1.Series[0]?.Points.RemoveAt(0);
                }

                double maxChartValue = 0;
                foreach (var point in chart1.Series[0]?.Points)
                {
                    if (point.YValues[0] > maxChartValue)
                        maxChartValue = point.YValues[0];
                }

                chart1.ChartAreas[0].AxisY.Maximum = maxChartValue * 1.1;
                */
                //addPoint(0, rps, 500, true);





                //change = rps - lastRps;
                ///lastRps = rps;
                //addPoint(1, rower.currentDt, 500, true, true);

                i += 4;
            }
        }

        /// <summary>
        /// Update method, gets run on a loop while the window is open.
        /// </summary>
        private void Update(object sender, EventArgs e)
        {
            addPoint(0, rower.currW, 300, true, false);
            addPoint(1, rower.currentDt, 300, true, true);

            //addPoint(0, lastRps, 300, true, false);
            //addPoint(1, change, 300, true, true);

            foreach (var connection in allSockets)
            {
                //connection.Send(lastRps.ToString());
                //called every frame
                connection.Send(this.rower.SendDataFormat());
            }
        }


        //Data incoming, event triggered
        //what data incoming-> cast or do something...
     /*   void OnMessage(string message)
        {
            lastMessage = message;
            log.Append(message);
            //Message indicate Cath or Finish
            this.rower.onStateSwitch(message);
        }
*/
        private void addPoint(int chartIndex, double value, int pointCount, bool scaleMax = false, bool scaleMin = false)
        {
            chart1.Series[chartIndex]?.Points.AddY(value);
            if (chart1.Series[chartIndex]?.Points.Count > pointCount)
            {
                //remove first index
                chart1.Series[chartIndex]?.Points.RemoveAt(0);
            }

            if (scaleMax)
            {
                double maxChartValue = 0;

                //series adjusted during runtime, so make a list beforehand
                foreach (var point in chart1.Series[chartIndex]?.Points)
                {
                    if (point.YValues[0] > maxChartValue)
                        maxChartValue = point.YValues[0];
                }
                chart1.ChartAreas[chartIndex].AxisY.Maximum = maxChartValue + (Math.Abs(maxChartValue) * 0.1) + 0.1;
            }

            if (scaleMin)
            {
                double minChartValue = 0;

                foreach (var point in chart1.Series[chartIndex]?.Points)
                {
                    if (point.YValues[0] < minChartValue)
                        minChartValue = point.YValues[0];
                }
                chart1.ChartAreas[chartIndex].AxisY.Minimum = minChartValue - (Math.Abs(minChartValue) * 0.1) - 0.1;
            }
        }
    }
}
