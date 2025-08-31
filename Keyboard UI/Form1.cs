using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;

namespace Keyboard_UI
{
    public partial class Form1 : Form
    {
        // Firebase Configuration
        IFirebaseConfig config = new FirebaseConfig
        {
            AuthSecret = "h9woUFmFKjA8y3PgpnBtbzIWjKqlrXSs1WcvClNM",
            BasePath = "https://keynergy-99be4-default-rtdb.firebaseio.com/"
        };

        IFirebaseClient client;

        private Label[] keys;
        private Dictionary<Label, int> keyPressCounts;
        private Dictionary<Label, Label> countLabels;
        private HashSet<Keys> keysPressed;  // Track which keys are currently pressed

        // Add these new fields to your Form1 class
        private DateTime currentDate = DateTime.Today;
        //private int forTotalKeypress = 0;
        private int todayTotalPresses = 0;
        //private double todayTotalCurrent = 0;
        //private double todayTotalVoltage = 0;

        private int localKeyPress = 0;
        private int firebasePresses = -1; // Use -1 as a "not yet fetched" marker

        // trial lang para no laggers
        private Dictionary<string, int> pendingFirebaseUpdates = new Dictionary<string, int>();
        private System.Timers.Timer firebaseUpdateTimer;
        private object firebaseLock = new object();

        private System.Timers.Timer dailyTotalsTimer;
        private readonly object dailyTotalsLock = new object();
        private bool isSavingDailyTotals = false;

        private System.Timers.Timer updateTotalsTimer;

        // for global keypress detection
        private NativeMethods.LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        // for pending of DailyTotal
        private bool isTotalUpdatePending = false;
        private readonly object totalUpdateLock = new object();

        // for reset of key's values
        private Dictionary<Keys, DateTime> keyPressTimes = new Dictionary<Keys, DateTime>();
        private const int ResetHoldTimeMs = 3000; // 3 seconds in milliseconds

        // serial comms
        private SerialPort serialPort;
        private string receivedData = "";
        private const string PICO_PORT = "COM9";
        private const int MAX_RECONNECT_ATTEMPTS = 5;
        private int reconnectAttempts = 0;
        private System.Timers.Timer reconnectTimer;

        // for voltage and current
        private double todayTotalVoltage = 0;
        private double todayTotalCurrent = 0;
        private DateTime lastEnergyUpdateTime = DateTime.Now;
        private System.Timers.Timer energyAccumulationTimer;
        private bool formHandleCreated = false;

        public Form1()
        {
            InitializeComponent();
            InitializeFirebaseUpdateTimer();
            InitializeTotalUpdateTimer();
            InitializeUpdateTotalsTimer();

            // for voltage and current
            this.HandleCreated += (sender, e) => {
                formHandleCreated = true;
                InitializeEnergyAccumulationTimer();
            };

            // serial comms
            InitializeSerialPortWithRetry();
            InitializeReconnectTimer();


            this.Load += Form1_Load;

            // for global keypress detection
            _proc = HookCallback;
            _hookID = SetHook(_proc);


            //this.KeyPreview = true; // Enable key event handling for the form
            //this.KeyDown += new KeyEventHandler(Form1_KeyDown); // Hook up KeyDown event
            //this.KeyUp += new KeyEventHandler(Form1_KeyUp); // Hook up KeyUp event

            //dataHistory.Hide(); // open this if may user control 1 na 
            //.ParentFormInstance = this;


            // Initialize Firebase client
            client = new FireSharp.FirebaseClient(config);
            if (client == null)
            {
                Console.WriteLine("Firebase connection failed!");
                Console.ReadLine();
                return;
            }




            // Initialize key labels and press counts
            keys = new Label[] { key0, key1, key2, key3, key4, key5, key6, key7, key8, key9, keyPeriod, keyAdd, keySubtract, keyMultiply, keyDivide, keyEnterKey, keyNumLock };
            keyPressCounts = new Dictionary<Label, int>();
            countLabels = new Dictionary<Label, Label>
            {
                { key0, labelKey0 },
                { key1, labelKey1 },
                { key2, labelKey2 },
                { key3, labelKey3 },
                { key4, labelKey4 },
                { key5, labelKey5 },
                { key6, labelKey6 },
                { key7, labelKey7 },
                { key8, labelKey8 },
                { key9, labelKey9 },
                { keyPeriod, labelKeyPeriod },
                { keyAdd, labelKeyAdd },
                { keySubtract, labelKeySubtract },
                { keyMultiply, labelKeyMultiply },
                { keyDivide, labelKeyDivide },
                { keyEnterKey, labelEnterKey },
                { keyNumLock, labelNumLock },
            };

            // Initialize key counts
            foreach (var key in keys)
            {
                keyPressCounts[key] = 0;
            }

            // Initialize keysPressed HashSet
            keysPressed = new HashSet<Keys>();

            // Call to load key press counts from Firebase
            _ = LoadKeyPressCountsFromFirebase();
            //_ =  LoadDailyHistoryFromFirebase();

        }

        /////////// for global keypress detection //////////////
        private IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc, NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        // for global keypress detection
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                // Simulate Form key press event


                this.Invoke(new Action(() =>
                {
                    Form1_KeyDown(this, new KeyEventArgs(key));
                }));
            }
            else if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYUP)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                this.Invoke(new Action(() =>
                {
                    Form1_KeyUp(this, new KeyEventArgs(key));
                }));
            }

            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        //////////////////////////////////////////////////////////////////////


        ////////////// for serial communication //////////////
        private void InitializeReconnectTimer()
        {
            reconnectTimer = new System.Timers.Timer(5000); // Check every 5 seconds
            reconnectTimer.Elapsed += (sender, e) => CheckAndReconnectPort();
            reconnectTimer.AutoReset = true;
            reconnectTimer.Enabled = true;
        }

        private void InitializeSerialPortWithRetry()
        {
            Console.WriteLine("[Serial] Initializing serial port with retry mechanism...");

            // First try the preferred port
            if (TryInitializePort(PICO_PORT))
            {
                return;
            }

            // If preferred port fails, scan all available ports
            ScanAndConnectToAvailablePort();
        }

        private bool TryInitializePort(string portName)
        {
            Console.WriteLine($"[Serial] Attempting to initialize {portName}...");

            try
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.Close();
                    serialPort.Dispose();
                }

                serialPort = new SerialPort(portName, 115200)
                {
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    RtsEnable = true,
                    DtrEnable = true,
                    NewLine = "\n",
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.ErrorReceived += SerialPort_ErrorReceived;

                if (!serialPort.IsOpen)
                {
                    serialPort.Open();
                    Console.WriteLine($"[Serial] Successfully opened {portName}");

                    // Test communication
                    serialPort.WriteLine("PING");
                    Console.WriteLine("[Serial] PING sent");

                    reconnectAttempts = 0; // Reset attempts on success
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Serial] Error initializing {portName}: {ex.Message}");
            }

            return false;
        }

        private void ScanAndConnectToAvailablePort()
        {
            Console.WriteLine("[Serial] Scanning for available ports...");
            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
            Console.WriteLine($"[Serial] Found ports: {string.Join(", ", ports)}");

            foreach (var port in ports)
            {
                if (TryInitializePort(port))
                {
                    Console.WriteLine($"[Serial] Connected to alternative port: {port}");
                    return;
                }
            }

            Console.WriteLine("[Serial] Could not connect to any available port");
            reconnectAttempts++;

            if (reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
            {
                Console.WriteLine("[Serial] Max reconnect attempts reached. Stopping retries.");
                reconnectTimer.Enabled = false;
            }
        }

        private void CheckAndReconnectPort()
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                Console.WriteLine("[Serial] Port not connected. Attempting to reconnect...");
                if (this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate {
                        ScanAndConnectToAvailablePort();
                    });
                }
                else
                {
                    // Handle case when window isn't ready yet
                    ScanAndConnectToAvailablePort();
                }
            }
            else
            {
                // Verify the port is still responsive
                try
                {
                    serialPort.WriteLine("PING");
                    Console.WriteLine("[Serial] Keep-alive PING sent");
                }
                catch
                {
                    Console.WriteLine("[Serial] Port appears to be disconnected");
                    serialPort.Close();
                }
            }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Console.WriteLine($"[Serial] Error received: {e.EventType}");
            // Trigger reconnection attempt
            this.Invoke((MethodInvoker)delegate {
                ScanAndConnectToAvailablePort();
            });
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                Console.WriteLine($"[Serial] DataReceived event triggered. EventType: {e.EventType}");

                if (!serialPort.IsOpen)
                {
                    Console.WriteLine("[Serial] Warning: Port is not open in DataReceived handler");
                    return;
                }

                int bytesToRead = serialPort.BytesToRead;
                Console.WriteLine($"[Serial] Bytes available: {bytesToRead}");

                if (bytesToRead == 0)
                {
                    Console.WriteLine("[Serial] No data available despite event");
                    return;
                }

                string newData = serialPort.ReadExisting();
                Console.WriteLine($"[Serial] Raw data received ({newData.Length} chars): {EscapeNonPrintable(newData)}");

                receivedData += newData;
                Console.WriteLine($"[Serial] Buffer content: {EscapeNonPrintable(receivedData)}");

                while (receivedData.Contains("\n"))
                {
                    int newlinePos = receivedData.IndexOf('\n');
                    string line = receivedData.Substring(0, newlinePos).Trim();
                    receivedData = receivedData.Substring(newlinePos + 1);

                    Console.WriteLine($"[Serial] Processing line: {EscapeNonPrintable(line)}");
                    Console.WriteLine($"[Serial] Remaining buffer: {EscapeNonPrintable(receivedData)}");

                    if (line.StartsWith("VOLTAGE:"))
                    {
                        string voltageStr = line.Substring(8);
                        Console.WriteLine($"[Serial] Found voltage string: {voltageStr}");

                        if (double.TryParse(voltageStr, out double voltage))
                        {
                            Console.WriteLine($"[Serial] Successfully parsed voltage: {voltage:F2}");

                            this.Invoke((MethodInvoker)delegate {
                                double durationSeconds = 5; // example duration
                                double timeHours = durationSeconds / 3600.0; // convert to hours
                                double current = 1.93;

                                double energyWh = voltage * current * timeHours * 1000;

                                labelVoltageSerial.Text = $"{energyWh:F2} μWh";
                                Console.WriteLine($"[UI] Updated labelVoltageSerial to: {energyWh:F4} Wh");
                            });
                        }
                        else
                        {
                            Console.WriteLine($"[Serial] Failed to parse voltage from: {voltageStr}");
                        }
                    }
                    else if (line.StartsWith("CURRENT:"))
                    {
                        string currentStr = line.Substring(8);
                        Console.WriteLine($"[Serial] Found current string: {currentStr}");

                        if (double.TryParse(currentStr, out double current))
                        {
                            Console.WriteLine($"[Serial] Successfully parsed current: {current:F2}");

                            this.Invoke((MethodInvoker)delegate {
                                //labelCurrentSerial.Text = $"{current:F2} A";
                                Console.WriteLine($"[UI] Updated labelCurrentSerial to: {current:F2} A");
                            });
                        }
                        else
                        {
                            Console.WriteLine($"[Serial] Failed to parse current from: {currentStr}");
                        }
                    }
                    else if (line.Length > 0)
                    {
                        Console.WriteLine($"[Serial] Received non-voltage line: {line}");
                    }
                }
            }
            catch (TimeoutException tex)
            {
                Console.WriteLine($"[Serial] Timeout: {tex.Message}");
            }
            catch (InvalidOperationException ioex)
            {
                Console.WriteLine($"[Serial] Port operation error: {ioex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Serial] Error in handler: {ex.Message}");
            }
        }

        // Helper method to make non-printable characters visible in console
        private string EscapeNonPrintable(string input)
        {
            return input.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");
        }
        //////////////////////////////////////////////////////


        private async void resetButton1_Click(object sender, EventArgs e)
        {
            await ResetKeyPressCountInFirebase("1");
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await LoadDailyHistoryFromFirebase();  // <-- Initial load
            await UpdateTotals();
            //dataHistory.UpdateGrandTotals();

            //this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            //this.WindowState = FormWindowState.Maximized;
            this.CenterToScreen();
        }



        private async void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            string keyPressed = string.Empty;

            //Console.WriteLine($"totoo: {e.KeyCode}");
            if (keysPressed.Contains(e.KeyCode)) return;  // If already pressed, do nothing

            // track when key is pressed
            if (!keyPressTimes.ContainsKey(e.KeyCode))
            {
                keyPressTimes[e.KeyCode] = DateTime.Now;
            }

            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
            {
                keyPressed = e.KeyCode.ToString().Substring(1); // Remove the 'D' prefix (e.g., D0 -> 0)
            }

            /*else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
            {
                keyPressed = e.KeyCode.ToString().Substring(6); // Remove the 'NumPad' prefix (e.g., NumPad0 -> 0)
            }*/

            else if (e.KeyCode == Keys.OemPeriod)
            {
                keyPressed = "."; // Set keyPressed to "."
            }
            else if (e.KeyCode == Keys.Decimal)
            {
                keyPressed = "."; // Set keyPressed to "."
            }

            else if (e.KeyCode == Keys.Add)
            {
                keyPressed = "+";  // Numpad +
            }
            else if (e.KeyCode == Keys.Subtract)
            {
                keyPressed = "-";  // Numpad -
            }
            else if (e.KeyCode == Keys.Multiply)
            {
                keyPressed = "*";  // Numpad *
            }
            else if (e.KeyCode == Keys.OemQuestion)
            {
                keyPressed = "/";  // Numpad /
            }
            else if (e.KeyCode == Keys.Enter)
            {
                keyPressed = "Enter";
            }
            else if (e.KeyCode == Keys.NumLock)
            {
                keyPressed = "NL";
            }

            // Log the key pressed
            if (!string.IsNullOrEmpty(keyPressed))
            {
                Console.WriteLine($"Key pressed: {keyPressed}");

                // Update key appearance and counts for the matching label
                foreach (var key in keys)
                {
                    if (key.Text == keyPressed)
                    {
                        // when button is pressed
                        if (keyPressCounts[key] >= 400)
                        {
                            key.BackColor = Color.FromArgb(231, 106, 94); // light red
                            key.ForeColor = Color.Black;
                        }
                        else if (keyPressCounts[key] >= 200)
                        {
                            key.BackColor = Color.FromArgb(255, 239, 166); // light yellow
                            key.ForeColor = Color.Black;
                        }
                        else
                        {
                            key.BackColor = Color.FromArgb(255, 201, 107); // light orange
                            key.ForeColor = Color.Black;
                        }

                        // Increment the count for the key
                        keyPressCounts[key]++;
                        countLabels[key].Text = $"{keyPressCounts[key]}";

                        localKeyPress++;


                        //Console.WriteLine($"totoo: {todayTotalPresses}");

                        lock (firebaseLock)
                        {
                            if (pendingFirebaseUpdates.ContainsKey(key.Text))
                                pendingFirebaseUpdates[key.Text] = keyPressCounts[key];
                            else
                                pendingFirebaseUpdates.Add(key.Text, keyPressCounts[key]);
                        }

                        await UpdateTotals();
                        lock (totalUpdateLock)
                        {
                            isTotalUpdatePending = true;
                        }

                        //_ = LoadDailyHistoryFromFirebase();
                    }
                }

                // Add the key to the set to prevent counting it again
                keysPressed.Add(e.KeyCode);

            }
        }


        private async Task LoadDailyHistoryFromFirebase()
        {
            try
            {
                //dataHistory.ClearData();
                FirebaseResponse response = await Task.Run(() => client.Get("DailyTotals"));

                if (response != null && response.Body != null)
                {
                    // Deserialize the response into a dictionary where the key is the date
                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(response.Body.ToString());
                    string today = DateTime.Now.ToString("yyyy-MM-dd");

                    foreach (var entry in allData.OrderByDescending(e => e.Key))
                    {
                        string date = entry.Key;

                        // skip today
                        if (date == today)
                            continue;

                        dynamic totals = entry.Value;

                        // Safely extract values from the dynamic object
                        int totalPresses = totals.TotalPresses != null ? (int)totals.TotalPresses : 0;
                        double totalCurrent = totals.TotalCurrent != null ? (double)totals.TotalCurrent : 0;
                        double totalVoltage = totals.TotalVoltage != null ? (double)totals.TotalVoltage : 0;

                        // Update the UI or control
                        //dataHistory.AddOrUpdateRow(date, totalPresses, totalVoltage, totalCurrent);


                        //Console.WriteLine($"Loaded: {date} | Presses: {totalPresses}, Current: {totalCurrent}, Voltage: {totalVoltage}");
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading daily history: {ex.Message}");
            }
        }


        // Add this new method to check for new day
        private void CheckForNewDay()
        {
            if (DateTime.Today > currentDate)
            {
                // New day detected - reset counts but keep historical data
                currentDate = DateTime.Today;
                todayTotalPresses = 0;
                todayTotalCurrent = 0;
                todayTotalVoltage = 0;

            }
        }

        private void AccumulateEnergy()
        {
            try
            {
                // Check if controls are still valid
                if (this.IsDisposed || labelVoltageSerial.IsDisposed)
                    return;

                DateTime now = DateTime.Now;
                double timeElapsed = (now - lastEnergyUpdateTime).TotalHours; // in hours

                if (double.TryParse(labelVoltageSerial.Text.Replace(" V", ""), out double currentVoltage))
                {
                    // Calculate energy since last update (Wh = V * A * h)
                    double voltageIncrement = currentVoltage * timeElapsed;
                    //double currentIncrement = currentCurrent * timeElapsed;

                    // Update daily totals
                    todayTotalVoltage += voltageIncrement;
                    //todayTotalCurrent += currentIncrement;

                    // Update the UI if controls exist
                    /*if (!labelTotalVoltage.IsDisposed)
                        labelTotalVoltage.Text = $"{todayTotalVoltage:F2} V";
                    if (!labelTotalCurrent.IsDisposed)
                        labelTotalCurrent.Text = $"{todayTotalCurrent:F2} A";*/

                    // Update last update time
                    lastEnergyUpdateTime = now;

                    // Flag for Firebase update
                    lock (totalUpdateLock)
                    {
                        isTotalUpdatePending = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Energy Accumulation] Error: {ex.Message}");
            }
        }


        // for total voltage and current
        private async Task UpdateTotals()
        {
            try
            {
                // Check if a new day has started
                CheckForNewDay();

                if (firebasePresses == -1)
                {
                    var dailyTotals = await GetDailyTotalsFromFirebase();
                    if (dailyTotals != null)
                    {
                        firebasePresses = (int)dailyTotals.TotalPresses;
                        todayTotalVoltage = dailyTotals.TotalVoltage ?? 0;
                        todayTotalCurrent = dailyTotals.TotalCurrent ?? 0;

                        // Update UI with loaded values
                        //labelTotalVoltage.Text = $"{todayTotalVoltage:F2} Vh";
                        //labelTotalCurrent.Text = $"{todayTotalCurrent:F2} Ah";
                    }
                }

                todayTotalPresses = firebasePresses + localKeyPress;

                //todayTotalVoltage = todayTotalPresses * 1.565;
                //todayTotalCurrent = todayTotalPresses * 0.7544;

                // Update UI
                //UpdateTotalsUI();

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                // Update the data history
                //dataHistory.todayAddOrUpdateRow(today, todayTotalPresses, todayTotalVoltage, todayTotalCurrent);


                // Fire-and-forget the Firebase update (don't await)
                _ = SaveDailyTotalsToFirebase();
                //dataHistory.UpdateGrandTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error calculating totals: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }



        public void UpdateTotalsUI(Label totalCurrentLabel, Label totalVoltageLabel)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateTotalsUI(totalCurrentLabel, totalVoltageLabel)));
                return;
            }

            //labelTotalCurrent.Text = totalCurrentLabel.Text;
            //labelTotalVoltage.Text = totalVoltageLabel.Text;
        }


        private async Task<dynamic> GetDailyTotalsFromFirebase()
        {
            return await Task.Run(() =>
            {
                try
                {
                    string today = DateTime.Now.ToString("yyyy-MM-dd");
                    FirebaseResponse response = client.Get($"DailyTotals/{today}");

                    if (response != null && response.Body != null)
                    {
                        // Deserialize the response into a dynamic object
                        var dailyTotals = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response.Body.ToString());
                        //Console.WriteLine($"Retrieved from Firebase - TotalPresses: {dailyTotals.TotalPresses}, TotalCurrent: {dailyTotals.TotalCurrent}, TotalVoltage: {dailyTotals.TotalVoltage}");
                        return dailyTotals;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error retrieving daily totals from Firebase: {ex.Message}");
                }

                return null; // Return null if there was an error or no data found
            });
        }

        private async void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            // check if held for 3 seconds
            if (keyPressTimes.TryGetValue(e.KeyCode, out var pressTime))
            {
                var holdTime = DateTime.Now - pressTime;
                if (holdTime.TotalMilliseconds >= ResetHoldTimeMs)
                {
                    // Map the Keys enum to your key text format
                    string keyText = MapKeyCodeToText(e.KeyCode);
                    if (!string.IsNullOrEmpty(keyText))
                    {
                        await ResetKeyPressCountInFirebase(keyText);
                    }
                }
                keyPressTimes.Remove(e.KeyCode); // Remove from tracking
            }

            // Reset the key appearance when the key is released
            foreach (var key in keys)
            {
                if (key.BackColor == Color.FromArgb(255, 236, 159)) // yellow
                {
                    key.BackColor = Color.FromArgb(237, 172, 46); // orange
                    key.ForeColor = Color.Black;
                }

                if (keyPressCounts[key] >= 400)
                {
                    key.BackColor = Color.FromArgb(192, 57, 43); // red
                    key.ForeColor = Color.Black;
                }

                else if (keyPressCounts[key] >= 200)
                {
                    key.BackColor = Color.FromArgb(252, 215, 95); // yellow
                    key.ForeColor = Color.Black;
                }

                else
                {
                    key.BackColor = Color.FromArgb(237, 172, 46); // orange
                    key.ForeColor = Color.Black;
                }
            }

            // Remove the key from the set when it is released
            keysPressed.Remove(e.KeyCode);
        }

        // for voltage and current
        private void InitializeEnergyAccumulationTimer()
        {
            // Only initialize if handle is created
            if (!formHandleCreated) return;

            energyAccumulationTimer = new System.Timers.Timer(1000); // Update every second
            energyAccumulationTimer.Elapsed += EnergyAccumulationTimer_Elapsed;
            energyAccumulationTimer.AutoReset = true;
            energyAccumulationTimer.Start();
        }

        private void EnergyAccumulationTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Safe invoke check
            if (!formHandleCreated || this.IsDisposed) return;

            try
            {
                // Use BeginInvoke instead of Invoke for better performance
                this.BeginInvoke(new Action(AccumulateEnergy));
            }
            catch (InvalidOperationException)
            {
                // Handle the case when the form is closing
                energyAccumulationTimer?.Stop();
            }
        }

        // every two seconds mag update sa firebase
        private void InitializeFirebaseUpdateTimer()
        {
            firebaseUpdateTimer = new System.Timers.Timer(2000); // 2 second interval
            firebaseUpdateTimer.Elapsed += async (sender, e) =>
            {
                await ProcessPendingFirebaseUpdates();
            };
            firebaseUpdateTimer.AutoReset = true;
            firebaseUpdateTimer.Start();
        }
        private void InitializeUpdateTotalsTimer()
        {
            updateTotalsTimer = new System.Timers.Timer(5000); // 30 seconds
            updateTotalsTimer.Elapsed += async (sender, e) =>
            {
                await GetDailyTotalsFromFirebase(); // Just sync with Firebase
                //await UpdateTotals(); // Refresh from Firebase if needed
            };
            updateTotalsTimer.AutoReset = true;
            updateTotalsTimer.Start();
        }

        private void InitializeTotalUpdateTimer()
        {
            var totalUpdateTimer = new System.Timers.Timer(5000); // every 5 seconds
            totalUpdateTimer.Elapsed += async (sender, e) =>
            {
                bool shouldRun = false;

                lock (totalUpdateLock)
                {
                    if (isTotalUpdatePending)
                    {
                        shouldRun = true;
                        isTotalUpdatePending = false;
                    }
                }

                if (shouldRun)
                {
                    await SaveDailyTotalsToFirebase(); // only run if flagged
                }
            };

            totalUpdateTimer.AutoReset = true;
            totalUpdateTimer.Start();
        }


        // to lessen yung lag (takes accumulated keypresses and send them to firebase in bulk)
        private async Task ProcessPendingFirebaseUpdates()
        {
            if (pendingFirebaseUpdates.Count == 0) return;


            Dictionary<string, int> updatesToProcess;
            lock (firebaseLock)
            {
                updatesToProcess = new Dictionary<string, int>(pendingFirebaseUpdates);
                pendingFirebaseUpdates.Clear();
            }


            foreach (var update in updatesToProcess)
            {
                try
                {
                    string firebaseKey = update.Key.Replace(".", "_dot").Replace("/", "_slash");
                    string firebasePath = $"KeyPressCounts/{firebaseKey}";

                    // Use Task.Run to make the synchronous Firebase call asynchronous
                    await Task.Run(() => client.Set(firebasePath, update.Value));
                    Console.WriteLine($"Updated Firebase: {firebaseKey} = {update.Value}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating Firebase: {ex.Message}");
                    // Requeue failed updates
                    lock (firebaseLock)
                    {
                        pendingFirebaseUpdates[update.Key] = update.Value;
                    }
                }
            }


        }

        private async Task LoadKeyPressCountsFromFirebase()
        {
            try
            {
                int totalPresses = 0;
                foreach (var key in keys)
                {

                    string firebaseKey = key.Text.Replace(".", "_dot").Replace("/", "_slash");
                    FirebaseResponse response = client.Get($"KeyPressCounts/{firebaseKey}");

                    if (response != null && response.Body != null)
                    {
                        // Parse the response into an integer (handle possible errors with try-catch)
                        if (int.TryParse(response.Body.ToString(), out int count))
                        {
                            keyPressCounts[key] = count;
                            // Update the corresponding label with the count
                            countLabels[key].Text = count.ToString();

                            // total presses 
                            totalPresses += count;

                            // Set background color based on count
                            if (keyPressCounts[key] >= 400)
                            {
                                key.BackColor = Color.FromArgb(192, 57, 43); // red
                                key.ForeColor = Color.Black;
                            }

                            else if (keyPressCounts[key] >= 200)
                            {
                                key.BackColor = Color.FromArgb(252, 215, 95); // yellow
                                key.ForeColor = Color.Black;
                            }

                            else
                            {
                                key.BackColor = Color.FromArgb(237, 172, 46); // orange
                                key.ForeColor = Color.Black;
                            }

                        }
                        else
                        {
                            // Handle the case where the response cannot be parsed into an integer
                            keyPressCounts[key] = 0;
                            countLabels[key].Text = "0";
                        }
                    }
                    else
                    {
                        // If no data is found, set the count to 0
                        keyPressCounts[key] = 0;
                        countLabels[key].Text = "0";
                    }
                }
                // Set today's totals
                //todayTotalPresses = totalPresses;
                //todayTotalVoltage = todayTotalPresses * 1.565;
                //todayTotalCurrent = todayTotalPresses * 0.7544;

                //await LoadHistoricalData(); // Add this line

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving data from Firebase: {ex.Message}");
            }


        }

        // Add this new method to save daily totals
        //private DateTime lastDailySave = DateTime.MinValue;
        private async Task SaveDailyTotalsToFirebase()
        {
            // Prevent overlapping saves
            lock (dailyTotalsLock)
            {
                if (isSavingDailyTotals) return;
                isSavingDailyTotals = true;
            }

            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string firebasePath = $"DailyTotals/{today}";

                var dailyData = new
                {
                    TotalPresses = todayTotalPresses,
                    TotalCurrent = todayTotalCurrent,
                    TotalVoltage = todayTotalVoltage,
                };

                // Add retry logic
                int retryCount = 0;
                bool success = false;

                while (retryCount < 3 && !success)
                {
                    try
                    {
                        var response = await Task.Run(() => client.Set(firebasePath, dailyData));
                        //Console.WriteLine($"Successfully saved daily totals for {today} to Firebase");
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        Console.WriteLine($"Attempt {retryCount} failed to save daily totals: {ex.Message}");
                        if (retryCount < 3)
                        {
                            await Task.Delay(2000); // Wait 2 seconds before retrying
                        }
                    }
                }
            }
            finally
            {
                lock (dailyTotalsLock)
                {
                    isSavingDailyTotals = false;
                }
            }
        }

        // Helper method to map Keys enum to your key text format
        private string MapKeyCodeToText(Keys keyCode)
        {
            switch (keyCode)
            {
                case Keys.D0: return "0";
                case Keys.D1: return "1";
                // ... add all your other key mappings
                case Keys.OemPeriod: return ".";
                case Keys.Decimal: return ".";
                case Keys.Add: return "+";
                case Keys.Subtract: return "-";
                case Keys.Multiply: return "*";
                case Keys.OemQuestion: return "/";
                case Keys.Enter: return "Enter";
                case Keys.NumLock: return "NL";
                default: return null;
            }
        }

        // Add this new method to reset a specific key
        private async Task ResetKeyPressCountInFirebase(string keyText)
        {
            try
            {
                // Find the corresponding label
                var key = keys.FirstOrDefault(k => k.Text == keyText);
                if (key == null) return;

                // Show confirmation dialog
                DialogResult result = MessageBox.Show(
                    $"Are you sure you want to reset the counter for key '{keyText}'?",
                    "Confirm Reset",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2); // Default to "No"

                // If user clicked No, cancel the reset
                if (result != DialogResult.Yes)
                {
                    return;
                }

                string firebaseKey = keyText.Replace(".", "_dot").Replace("/", "_slash");
                string firebasePath = $"KeyPressCounts/{firebaseKey}";

                // Show loading/processing indication
                Cursor.Current = Cursors.WaitCursor;
                key.BackColor = Color.LightGray; // Visual feedback that reset is in progress

                // Reset value in Firebase
                await Task.Run(() => client.Set(firebasePath, 0));

                // Reset local count and update UI
                keyPressCounts[key] = 0;
                countLabels[key].Text = "0";
                key.BackColor = Color.FromArgb(237, 172, 46); // default orange
                key.ForeColor = Color.Black;

                // Update totals since we've changed a key's count
                await UpdateTotals();

                // Show success message
                MessageBox.Show(
                    $"The counter for key '{keyText}' has been successfully reset to 0.",
                    "Reset Successful",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error resetting key data: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default; // Restore normal cursor
            }
        }

        // when delete all
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            //for serial
            if (serialPort != null)
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
                serialPort.Dispose();
            }

            energyAccumulationTimer?.Stop();
            energyAccumulationTimer?.Dispose();

            // Stop timers first
            updateTotalsTimer?.Stop();
            dailyTotalsTimer?.Stop();
            firebaseUpdateTimer?.Stop();

            // Force a final save (await it to complete)
            await SaveDailyTotalsToFirebase();

            // Then dispose resources
            updateTotalsTimer?.Dispose();
            dailyTotalsTimer?.Dispose();
            firebaseUpdateTimer?.Dispose();

            base.OnFormClosing(e);
        }

        private void homeButton_Click_1(object sender, EventArgs e)
        {
            //dataHistory.Hide();
        }
        private async void dataButton_Click_1(object sender, EventArgs e)
        {
            //dataHistory.Show();
            //dataHistory.BringToFront();

            //lastRowUpdateTimes.Clear();
            //dataHistory.ClearRows();
            await LoadDailyHistoryFromFirebase();
        }

        private void bellaButton_Click_1(object sender, EventArgs e)
        {
            MessageBox.Show($"I love you my bellapotpot");
        }

        private void textBox26_TextChanged(object sender, EventArgs e)
        {

        }
    }

    // for global keypress detection
    public class NativeMethods
    {
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
    }

}