using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Sockets;
using System.Net;
using System.Windows.Interop;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using WindowsInput;


namespace ProjectMalnatiServer
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window
    {
        bool isX = true;
        string coordX = "";
        string coordY = "";
        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;
        string hostname;
        string pass;
        byte[] check = new byte[64];
        Win32.POINT p = new Win32.POINT();
        private Dispatcher dispatcher;
        Socket acceptedSocket;
        IPEndPoint myEP;

        delegate void performConnection();
        performConnection pc;
        Thread workerThreadConnection;
        Thread workerThread;

        private volatile bool _shouldStop;
        private bool connesso;
        private bool  client=false;
        bool isValidIp = true;
        bool isValidPorta = true;
        bool isValidPass = true;

        public MainWindow()
        {
            InitializeComponent();

            hostname = Dns.GetHostName();

            IPHostEntry ipEntry = Dns.GetHostEntry(hostname);
            IPAddress[] addresses = ipEntry.AddressList;

            Console.WriteLine("Computer Host Name = " + hostname);

            for (int i = 0; i < addresses.Length; i++)
            {
                Console.WriteLine("IP Address n.{0} = {1} ", i, addresses[i].ToString());
                if (addresses[i].ToString().Length <= 16)
                {
                    label_ip_local.Content = addresses[i].ToString();
                    textBoxIP.Text = addresses[i].ToString();
                }
            }
            
            //label_ip_local.Foreground = Brushes.Green;
            label_ip_local.FontSize = 18;
            label_ip_local.FontWeight = FontWeights.Medium;
            listeningTextBlock.Text = "";

            //assegno coordinate fittizie a p per inizializzarlo
            p.x = 500;
            p.y = 200;

            connesso = false;

            dispatcher = Dispatcher.CurrentDispatcher;

            //da aggiornare!
            //myEP = new IPEndPoint(IPAddress.Parse(this.textBoxIP.Text), Convert.ToInt16(this.textBoxPort.Text));

            pc = new performConnection(() =>
            {
                connesso = true;
                this.listeningTextBlock.Text = "";
                Console.WriteLine("Server connesso" + "\n");
                this.buttonListen.Content = "Disconnect";
                this.buttonListen.IsEnabled = true; //il bottone ora permette la disconnessione dal client
                this.WindowState = WindowState.Minimized;
                //this.Hide();
                //workerThreadConnection.Join(); //aspetto che il thread di creaz connessione ritorna
                workerThread = new Thread(MyReceive); //creo un nuovo thread solo per gestire la connessione
                workerThread.Start();
                //MyReceive();
            });
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void listenSocket(object sender, RoutedEventArgs e)
        {
            //il bottone può fare due cose, connettere o disconnettere, occorre distinguere 
            //i due casi

            if (this.connesso == false && this.buttonListen.Content != "Cancel")
            //if (this.connesso == false)
            {
                //this.buttonListen.IsEnabled = false;
                IPAddress address;
                Int16 port;

                if (!IPAddress.TryParse(this.textBoxIP.Text, out address))
                {
                    isValidIp = false;
                    MessageBox.Show("IP non valido o mancante");
                }

                if (!Int16.TryParse(this.textBoxPort.Text, out port))
                {
                    isValidPorta = false;
                    MessageBox.Show("Porta non valida o mancante");
                }

                if (this.textBoxPassword.Password.Length==0)
                {
                    isValidPass = false;
                    MessageBox.Show("Password mancante");
                }

                if (isValidPass == true && isValidPorta == true && isValidIp == true)
                {

                    myEP = new IPEndPoint(IPAddress.Parse(this.textBoxIP.Text), Convert.ToInt16(this.textBoxPort.Text));
                    this.buttonListen.Content = "Cancel";
                    this.listeningTextBlock.Text = "Listening...";
                    this.textBoxIP.IsEnabled = false;
                    this.textBoxPort.IsEnabled = false;
                    this.textBoxPassword.IsEnabled = false;
                    this.pass = this.textBoxPassword.Password;
                    workerThreadConnection = new Thread(connetti);
                    workerThreadConnection.Start();
                }
                
            }
            else
            // else if(this.connesso == true && this.buttonListen.Content == "Cancel")
            {
                //MessageBox.Show("Ho premuto su Cancel o Disconnetti sto per annullare la richiesta!");
                disconnetti();
            }
        }

        //il metodo è richiamato sia alla pressione del bottone disconnetti sia
        //in chiusura della finestra principale dell'applicazione
        private void disconnetti()
        {
            _shouldStop = true; //segnalo al thread che deve terminare
            if (workerThreadConnection != null)
            {
                //workerThreadConnection.Abort();
                workerThreadConnection.Join();
            }
            if (workerThread != null)
            {
                //workerThread.Abort();
               // workerThread.Join();
            }
            if (acceptedSocket != null)
            {
                if (client == true)
                {
                    if (acceptedSocket.Connected == true)
                    {
                        acceptedSocket.Shutdown(SocketShutdown.Both);
                        acceptedSocket.Close();
                    }

                    this.WindowState = WindowState.Maximized;
                    MessageBox.Show("Client ha chiuso connessione!");
                    
                }
                else
                {
                    if(acceptedSocket.Connected==true){
                    acceptedSocket.Shutdown(SocketShutdown.Both);
                    acceptedSocket.Close();
                    }
                    this.WindowState = WindowState.Maximized;
                }
                //this.Show();
                //acceptedSocket.Disconnect(true);
            }

            this.connesso = false;
            this.textBoxIP.IsEnabled = true;
            this.textBoxPort.IsEnabled = true;
            this.textBoxPassword.IsEnabled = true;
            this.textBoxPassword.Password = "";
            this.buttonListen.IsEnabled = true;
            this.buttonListen.Content = "Listen";
            this.listeningTextBlock.Text = "";
            
            _shouldStop = false; //nel caso non si tratti di chiusura dell'applicazione
        }

        private void connetti()
        {
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket handler = null;
            _shouldStop = false;

            try
            {
                listener.Bind(myEP);
                listener.Listen(50);
                IPEndPoint localEndPoint = (IPEndPoint)listener.LocalEndPoint;
                Console.WriteLine("Listening on {0}:{1}", localEndPoint.Address, localEndPoint.Port);


                while (!_shouldStop)
                {
                    if (listener.Poll(2000, SelectMode.SelectRead) == true) //controlla se qualcuno gli ha inviato qualcosa
                    {
                        //solo quando sono sicuro di avere richieste in coda con Poll faccio accept
                        handler = listener.Accept();
                        //handler.ReceiveTimeout = 5000; //per rendere la receive "non bloccante"
                        //inutile perchè quando il client fa connetti subito manda la password 
                        //sbloccando la receive

                        int numBytes = handler.Receive(check);

                        //controllo password
                        if (numBytes > 0)
                        {
                            if (Encoding.UTF8.GetString(check).Trim('\0').Equals(this.pass))
                            {
                                handler.Send(check);
                                acceptedSocket = handler;
                                acceptedSocket.ReceiveTimeout = 5000;
                                break;
                            }
                            else
                            {
                                handler.Send(Encoding.UTF8.GetBytes("N"));
                                //handler.Shutdown(SocketShutdown.Both);
                                //handler.Disconnect(true); //chiude la connessione ma lascia il socket riutilizzabile
                                //handler.Close();
                                //continue;
                            }
                        }
                    }
                }
                if (!_shouldStop)
                {
                    //connessione valida stabilita
                    IPEndPoint remoteEndPoint = (IPEndPoint)acceptedSocket.RemoteEndPoint;
                    Console.WriteLine("Accepted connection from {0}:{1}.", remoteEndPoint.Address, remoteEndPoint.Port);
                    dispatcher.Invoke(pc);
                    return;
                }
                if (listener.Connected == true) //se ho stabilito una connessione ma poi ho chiuso il programma
                    listener.Shutdown(SocketShutdown.Both);
                listener.Close();
                Console.WriteLine("Il thread connetti sta per terminare\n");
                return;
            }
            catch (SocketException)
            {
                Console.WriteLine("Errore in fase di ascolto sul socket\n");
                if (handler != null && handler.Connected == true)
                    handler.Close();
                disconnetti();
            }
            catch (System.FormatException) { Console.WriteLine("Errore!"); }
            catch (System.OverflowException) { Console.WriteLine("Errore!"); }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }

        

        private void parseFunction(string bufferString)
        {
            String car = null;
            bool isChar=false;
            int count = 0;
            
            foreach (char ch in bufferString)
            {
                if (ch == '\0')
                    return;

                if (ch == 'U')
                {
                    Action mouseLeftDownOrUp = () => { Win32.mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)p.x, (uint)p.y, 0, 0); };
                    dispatcher.Invoke(mouseLeftDownOrUp);
                    continue;
                }

                if (ch == 'D')
                {
                    Action mouseLeftDown = () => { Win32.mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)p.x, (uint)p.y, 0, 0); };
                    dispatcher.Invoke(mouseLeftDown);
                    continue;
                }

                if (ch == 'R')
                {
                    Action mouseRightDownOrUp = () => { Win32.mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, (uint)p.x, (uint)p.y, 0, 0); };
                    dispatcher.Invoke(mouseRightDownOrUp);
                    continue;
                }

                if (ch != '?' && ch != ';' && ch!= '-')
                {
                    if (isChar == true)
                    {
                        car+= ch;
                        count++;
                        if (count == 2) {
                            count = 0;
                          
                            
                            /*_____________________________________________________________*/


                            if (car == "1")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LBUTTON);
                            }

                            if (car == "2")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RBUTTON);
                            }
                            if (car == "3")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.CANCEL);
                            }
                            if (car == "4")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MBUTTON);
                            }
                            if (car == "5")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.XBUTTON1);
                            }
                            if (car == "6")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.XBUTTON2);
                            }
                            if (car == "8")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BACK);
                            }
                            if (car == "9")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.TAB);
                            }

                            if (car == "C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.CLEAR);
                            }
                            if (car == "D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RETURN);
                            }
                            if (car == "10")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SHIFT);
                            }
                            if (car == "11")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.CONTROL);
                            }
                            if (car == "12")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MENU);
                            }
                            if (car == "13")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PAUSE);
                            }
                            if (car == "14")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.CAPITAL);
                            }
                            if (car == "15")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.KANA);
                            }
                            if (car == "15")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.HANGEUL);
                            }
                            if (car == "17")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.JUNJA);
                            }
                            if (car == "18")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.FINAL);
                            }

                            if (car == "19")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.HANJA);
                            }
                            if (car == "19")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.KANJI);
                            }
                            if (car == "1B")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.ESCAPE);
                            }
                            if (car == "1C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.CONVERT);
                            }
                            if (car == "1D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NONCONVERT);
                            }
                            if (car == "1E")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.ACCEPT);
                            }
                            if (car == "1F")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MODECHANGE);
                            }
                            if (car == "20")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SPACE);
                            }

                            if (car == "21")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PRIOR);
                            }
                            if (car == "22")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NEXT);
                            }
                            if (car == "23")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.END);
                            }
                            if (car == "24")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.HOME);
                            }
                            if (car == "25")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LEFT);
                            }
                            if (car == "26")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.UP);
                            }
                            if (car == "27")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RIGHT);
                            }
                            if (car == "28")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.DOWN);
                            }
                            if (car == "29")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SELECT);
                            }
                            if (car == "2A")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PRINT);
                            }
                            if (car == "2B")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.EXECUTE);
                            }

                            if (car == "2C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SNAPSHOT);
                            }
                            if (car == "2D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.INSERT);
                            }
                            if (car == "2E")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.DELETE);
                            }
                            if (car == "2F")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.HELP);
                            }
                            if (car == "30")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_0);
                            }
                            if (car == "31")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_1);
                            }
                            if (car == "32")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_2);
                            }
                            if (car == "33")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_3);
                            }
                            if (car == "34")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_4);
                            }
                            if (car == "35")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_5);
                            }
                            if (car == "36")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_6);
                            }
                            if (car == "37")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_7);
                            }
                            if (car == "38")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_8);
                            }
                            if (car == "39")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_9);
                            }
                            if (car == "41")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_A);
                            }
                            if (car == "42")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_B);
                            }
                            if (car == "43")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_C);
                            }
                            if (car == "44")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_D);
                            }
                            if (car == "45")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_E);
                            }
                            if (car == "46")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_F);
                            }
                            if (car == "47")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_G);
                            }
                            if (car == "48")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_H);
                            }
                            if (car == "49")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_I);
                            }
                            if (car == "4A")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_J);
                            }
                            if (car == "4B")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_K);
                            }
                            if (car == "4C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_L);
                            }
                            if (car == "4D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_M);
                            }
                            if (car == "4E")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_N);
                            }
                            if (car == "4F")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_O);
                            }
                            if (car == "50")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_P);
                            }
                            if (car == "51")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_Q);
                            }
                            if (car == "52")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_R);
                            }
                            if (car == "53")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_S);
                            }
                            if (car == "54")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_T);
                            }
                            if (car == "55")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_U);
                            }
                            if (car == "56")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_V);
                            }
                            if (car == "57")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_W);
                            }
                            if (car == "58")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_X);
                            }
                            if (car == "59")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_Y);
                            }
                            if (car == "5A")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_Z);
                            }
                            if (car == "5B")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LWIN);
                            }
                            if (car == "5C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RWIN);
                            }
                            if (car == "5D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.APPS);
                            }
                            if (car == "5F")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SLEEP);
                            }
                            if (car == "60")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD0);
                            }
                            if (car == "61")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD1);
                            }
                            if (car == "62")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD2);
                            }
                            if (car == "63")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD3);
                            }
                            if (car == "64")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD4);
                            }
                            if (car == "65")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD5);
                            }
                            if (car == "66")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD6);
                            }
                            if (car == "67")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD7);
                            }
                            if (car == "68")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD8);
                            }

                            if (car == "69")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMPAD9);
                            }
                            if (car == "6A")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MULTIPLY);
                            }
                            if (car == "6B")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.ADD);
                            }
                            if (car == "6C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SEPARATOR);
                            }
                            if (car == "6D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SUBTRACT);
                            }
                            if (car == "6F")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.DIVIDE);
                            }
                            if (car == "70")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F1);
                            }
                            if (car == "71")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F2);
                            }
                            if (car == "72")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F3);
                            }
                            if (car == "73")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F4);
                            }
                            if (car == "74")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F5);
                            }
                            if (car == "75")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F6);
                            }
                            if (car == "76")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F7);
                            }
                            if (car == "77")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F8);
                            }
                            if (car == "78")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F9);
                            }
                            if (car == "79")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F10);
                            }
                            if (car == "7A")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F11);
                            }
                            if (car == "7B")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F12);
                            }
                            if (car == "7C")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F13);
                            }
                            if (car == "7D")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F14);
                            }
                            if (car == "7E")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F15);
                            }
                            if (car == "7F")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F16);
                            }
                            if (car == "80")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F17);
                            }
                            if (car == "81")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F18);
                            }
                            if (car == "82")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F19);
                            }
                            if (car == "83")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F20);
                            }
                            if (car == "84")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F21);
                            }
                            if (car == "85")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F23);
                            }
                            if (car == "86")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F23);
                            }
                            if (car == "87")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.F24);
                            }
                            if (car == "90")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NUMLOCK);
                            }
                            if (car == "91")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.SCROLL);
                            }
                            if (car == "92")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_1);
                            }
                            if (car == "93")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_2);
                            }
                            if (car == "94")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_3);
                            }
                            if (car == "95")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_4);
                            }
                            if (car == "96")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_5);
                            }
                            if (car == "A0")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LSHIFT);
                            }
                            if (car == "A1")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RSHIFT);
                            }
                            if (car == "A2")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LCONTROL);
                            }
                            if (car == "A3")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RCONTROL);
                            }
                            if (car == "A4")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LMENU);
                            }
                            if (car == "A5")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.RMENU);
                            }
                            if (car == "A6")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_BACK);
                            }
                            if (car == "A7")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_FORWARD);
                            }
                            if (car == "A8")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_REFRESH);
                            }
                            if (car == "A9")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_STOP);
                            }
                            if (car == "AA")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_SEARCH);
                            }
                            if (car == "AB")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_FAVORITES);
                            }
                            if (car == "AC")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.BROWSER_HOME);
                            }
                            if (car == "AD")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VOLUME_MUTE);
                            }
                            if (car == "AE")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VOLUME_DOWN);
                            }
                            if (car == "AF")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.VOLUME_UP);
                            }
                            if (car == "B0")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MEDIA_NEXT_TRACK);
                            }
                            if (car == "B1")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MEDIA_PREV_TRACK);
                            }
                            if (car == "B2")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MEDIA_STOP);
                            }
                            if (car == "B3")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.MEDIA_PLAY_PAUSE);
                            }
                            if (car == "B5")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LAUNCH_MEDIA_SELECT);
                            }
                            if (car == "B6")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LAUNCH_APP1);
                            }
                            if (car == "B7")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.LAUNCH_APP2);
                            }
                            if (car == "BA")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_1);
                            }
                            if (car == "BB")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_PLUS);
                            }
                            if (car == "BC")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_COMMA);
                            }
                            if (car == "BD")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_MINUS);
                            }
                            if (car == "BE")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_PERIOD);
                            }
                            if (car == "BF")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_2);
                            }
                            if (car == "C0")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_3);
                            }
                            if (car == "DB")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_4);
                            }
                            if (car == "DC")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_5);
                            }
                            if (car == "DD")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_6);
                            }
                            if (car == "DE")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_7);
                            }
                            if (car == "DF")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_8);
                            }
                            if (car == "E2")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_102);
                            }
                            if (car == "E5")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PROCESSKEY);
                            }
                            if (car == "E7")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PACKET);
                            }
                            if (car == "F6")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.ATTN);
                            }
                            if (car == "F7")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.CRSEL);
                            }
                            if (car == "F8")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.EXSEL);
                            }
                            if (car == "F9")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.EREOF);
                            }
                            if (car == "FA")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PLAY);
                            }
                            if (car == "FB")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.ZOOM);
                            }
                            if (car == "FC")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.NONAME);
                            }
                            if (car == "FD")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.PA1);
                            }
                            if (car == "FE")
                            {
                                InputSimulator.SimulateKeyPress(VirtualKeyCode.OEM_CLEAR);
                            }
                                                    }
                        continue;
                    }
                    else
                    {
                        if (isX)
                        { coordX += Convert.ToString(ch); }
                        else
                        { coordY += Convert.ToString(ch); }
                    }
                }
                else
                {
                    if (ch == ';')
                        isX = false;
                    
                    if (ch == '-')
                    {
                        if (isChar == true)
                        {
                            isChar = false;
                        }
                        else
                        {
                            isChar = true;
                        }
                        continue;

                    }
                    else if (ch == '?')
                    {
                        isX = true;
                        if (coordX != "" && coordY != "")
                        {
                            double x_rel = Convert.ToDouble(coordX);
                            double y_rel = Convert.ToDouble(coordY);

                            p.x = Convert.ToInt16(x_rel * 1920);
                            p.y = Convert.ToInt16(y_rel * 1200);

                            coordX = "";
                            coordY = "";

                            //Console.WriteLine("x:" + p.x + " y:" + p.y + "\n");

                            Action setCurPos = () => { Win32.SetCursorPos(p.x, p.y); };
                            dispatcher.Invoke(setCurPos);
                        }
                    }
                }
            }
        }
        //uu

        public static void BeginSendCallback(IAsyncResult ar) { }
        public void MyReceive()
        {
            while (!_shouldStop)
            {
                try
                {
                    byte[] buffer = new byte[50];
                    string bufferString;
                    byte[] stringa = Encoding.UTF8.GetBytes("D");
                    acceptedSocket.BeginSend(stringa,0,stringa.Length,SocketFlags.None, BeginSendCallback, acceptedSocket);

                    int numBytes = acceptedSocket.Receive(buffer); //questa receive e' "non bloccante"
                    
                    if (numBytes > 0)
                    {
                        bufferString = Encoding.UTF8.GetString(buffer);
                        parseFunction(bufferString);
                    }
                }

                catch (SocketException se)
                {
                    Console.WriteLine("errore con codice: " + se.ErrorCode);
                    //10060
                     if (se.ErrorCode != 10060) //perchè se timeout scatta viene lanciata, ma non occorre reagire!
                      //if(se.ErrorCode==10054) //10054 quando il client fa disconnetti o cancella..io faccio la send ma mi da questa eccezione..
                        {
                        Action showWindow = () => {
                            client = true;
                            disconnetti();
                        };
                        dispatcher.Invoke(showWindow);
                        break;
                    }
                      
                }
            }
            Console.WriteLine("Il thread myReceive sta per terminare\n");
        }

   private void closeWindowOperations(object sender, System.ComponentModel.CancelEventArgs e)
        {
           // MessageBox.Show("Ho chiuso finestra e ora annullo tutto!");
            
            if (acceptedSocket != null)
                disconnetti();
            //MessageBox.Show("Sto chiudendo finestra e non sono connesso!");
            this.Hide();
            
        }
    }

}