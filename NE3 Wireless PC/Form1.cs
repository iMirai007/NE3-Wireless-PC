using NE3_Wireless_PC.Model;
using OpenCvSharp;
using System;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Header = NE3_Wireless_PC.Model.Header;
using Window = OpenCvSharp.Window;
namespace NE3_Wireless_PC
{
    public partial class Form1 : Form
    {
        private ManagementEventWatcher watcher;
        private string lastConnectionStatus = "";
        private static readonly string BroadcastIp = "192.168.1.255";
        private static IPAddress deviceIp = null;
        private static Socket _socket;
        private static int? lastFrameNumber = null;
        private static Frame currentFrame = null;
        private static int currentAngle = 0;
        private CancellationTokenSource cts;

        public Form1()
        {
            InitializeComponent();
            StartWiFiWatcher();
        }

        private void StartWiFiWatcher()
        {
            string query = "SELECT * FROM __InstanceModificationEvent WITHIN 1 " +
                           "WHERE TargetInstance ISA 'Win32_NetworkAdapter' AND (TargetInstance.NetConnectionStatus IS NOT NULL OR TargetInstance.NetEnabled IS NOT NULL)";

            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += OnNetworkAdapterChanged;
            watcher.Start();
        }

        private void OnNetworkAdapterChanged(object sender, EventArrivedEventArgs e)
        {
            var networkAdapter = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string adapterName = networkAdapter["Name"].ToString();
            string netConnectionStatus = networkAdapter["NetConnectionStatus"]?.ToString();
            string netEnabled = networkAdapter["NetEnabled"]?.ToString();

            // Handle adapter state changes
            if (netEnabled == "False")
            {
                HandleDisconnection("Wi-Fi turned off.");
            }
            else if (netConnectionStatus != lastConnectionStatus)
            {
                lastConnectionStatus = netConnectionStatus;

                if (netConnectionStatus == "2") // Connected
                {
                    HandleConnection();
                }
                else if (netConnectionStatus == "0") // Disconnected
                {
                    HandleDisconnection("Wi-Fi disconnected.");
                }
            }
        }

        private void HandleConnection()
        {
            Task.Run(() =>
            {
                var (s, jsonData) = CheckPorts();
                if (!string.IsNullOrWhiteSpace(jsonData))
                {
                    Invoke((Action)(() => button1.Enabled = true)); // Update UI safely
                    _socket = s;
                }
            });
        }

        private void HandleDisconnection(string message)
        {
            Invoke((Action)(() =>
            {
                lastConnectionStatus = "";
                button1.Enabled = false;
                MessageBox.Show(message, "Network Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        }

        private (Socket, string) CheckPorts()
        {
            Socket socket = CreateSocket();
            if (socket == null) return (null, null);

            SendDiscoveryPacket(socket);

            try
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = new byte[1500];
                int received = socket.ReceiveFrom(buffer, ref remoteEndPoint);

                byte[] receivedData = new byte[received];
                Array.Copy(buffer, receivedData, received);

                deviceIp = ((IPEndPoint)remoteEndPoint).Address;
                return (socket, Encoding.UTF8.GetString(receivedData));
            }
            catch (SocketException)
            {
                socket.Dispose(); // Dispose of the socket in case of an exception
                return (null, null);
            }
        }


        private static Socket CreateSocket()
        {
            try
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    ReceiveTimeout = 1000
                };
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, 36123));
                return socket;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SendDiscoveryPacket(Socket socket)
        {
            try
            {
                byte[] packet = { 0x66, 0x39, 0x01, 0x01 };
                IPEndPoint broadcastEndPoint1 = new IPEndPoint(IPAddress.Parse(BroadcastIp), 58090);
                IPEndPoint broadcastEndPoint2 = new IPEndPoint(IPAddress.Parse(BroadcastIp), 46526);

                socket.SendTo(packet, broadcastEndPoint1);
                socket.SendTo(packet, broadcastEndPoint2);
            }
            catch (Exception)
            {
                // Log exception (optional)
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            HandleConnection();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_socket != null)
            {
                _socket.Close();
                _socket.Dispose();
                _socket = null;
            }
            watcher?.Stop();
            watcher?.Dispose();
            cts?.Cancel();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (cts != null)
            {
                cts?.Cancel();
                button1.Text = "Start";
                return;
            }
            button1.Text = "Stop";
            cts = new CancellationTokenSource();
            var token = cts.Token;
            try
            {
                await Task.Run(() => StartListening(token), token);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Operation canceled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
            finally
            {
                cts = null;
            }
        }
        private void StartListening(CancellationToken token)
        {
            _socket.ReceiveTimeout = 250;
            _socket.SendTo(new byte[] { 0x86, 0x06, 0x01 }, new IPEndPoint(deviceIp, 52219));
            _socket.SendTo(new byte[] { 0x20, 0x36 }, new IPEndPoint(deviceIp, 44506));
            using (Window window = new Window("image"))
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] buffer = new byte[1500];
                        int received;
                        received = _socket.ReceiveFrom(buffer, ref remoteEndPoint);
                        if (received == 0) continue;

                        byte[] receivedData = new byte[received];
                        Array.Copy(buffer, receivedData, received);
                        IPEndPoint remoteIpEndPoint = remoteEndPoint as IPEndPoint;

                        if (remoteIpEndPoint.Port == 44506)
                        {
                            ProcessImageData(receivedData, window);
                        }
                        else if (remoteIpEndPoint.Port == 52219)
                        {
                            ProcessAngleData(receivedData);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        MessageBox.Show("Socket has been disposed. Stopping listener.");
                        break;
                    }
                    catch (SocketException ex)
                    {
                        if (token.IsCancellationRequested)
                            break; // Graceful exit on cancellation
                        MessageBox.Show($"Socket error: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Unexpected error: {ex.Message}");
                    }
                }
            }
        }
        private void ProcessImageData(byte[] receivedData, Window window)
        {
            Header header = new Header(receivedData);
            if (header.FrameNumber != lastFrameNumber)
            {
                currentFrame = new Frame(header);
                lastFrameNumber = header.FrameNumber;
            }

            currentFrame.Add(receivedData);
            if (currentFrame != null && currentFrame.Complete())
            {
                byte[] imageData = currentFrame.Data();
                if (imageData.Length > 0)
                {
                    try
                    {
                        Mat imgCv = Cv2.ImDecode(imageData, ImreadModes.Color);
                        if (!imgCv.Empty())
                        {
                            var firstScreen = Screen.AllScreens[0];
                            int screenWidth = firstScreen.Bounds.Width;
                            int screenHeight = firstScreen.Bounds.Height;
                            Cv2.Resize(imgCv, imgCv, new OpenCvSharp.Size(screenWidth, screenHeight), 2.0, 2.0, InterpolationFlags.Linear);
                            imgCv = RotateImage(imgCv, -currentAngle);

                            window.ShowImage(imgCv);
                            Cv2.WaitKey(2);
                        }
                    }
                    catch (Exception)
                    {
                        // Handle image processing error
                    }
                }
            }
        }
        private void ProcessAngleData(byte[] receivedData)
        {
            if (receivedData.Length >= 20)
            {
                byte x = receivedData[1];
                byte y = receivedData[3];
                byte z = receivedData[5];
                ushort a = BitConverter.ToUInt16(new byte[] { receivedData[16], receivedData[17] }, 0);
                currentAngle = a;
            }
        }

        private static Mat RotateImage(Mat image, double angle)
        {
            Point2f imageCenter = new Point2f(image.Width / 2.0f, image.Height / 2.0f);
            Mat rotationMatrix = Cv2.GetRotationMatrix2D(imageCenter, angle, 1.0);
            Mat result = new Mat();
            Cv2.WarpAffine(image, result, rotationMatrix, image.Size(), InterpolationFlags.Linear);
            return result;
        }
    }
}
