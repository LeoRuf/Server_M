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

                if (ch == 'Q')
                {
                    Console.WriteLine("q");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_Q);
                    continue;
                }
                if (ch == 'W')
                {
                    Console.WriteLine("w");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_W);
                    continue;
                }
                if (ch == 'E')
                {
                    Console.WriteLine("e");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_E);
                    continue;
                }
                if (ch == 'R')
                {
                    Console.WriteLine("r");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_R);
                    continue;
                }
                if (ch == 'T')
                {
                    Console.WriteLine("t");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_T);
                    continue;
                }
                if (ch == 'Y')
                {
                    Console.WriteLine("y");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_Y);
                    continue;
                }
                if (ch == 'U')
                {
                    Console.WriteLine("u");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_U);
                    continue;
                }
                if (ch == 'I')
                {
                    Console.WriteLine("i");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_I);
                    continue;
                }
                if (ch == 'O')
                {
                    Console.WriteLine("o");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_O);
                    continue;
                }
                if (ch == 'P')
                {
                    Console.WriteLine("p");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_H);
                    continue;
                }
                if (ch == 'A')
                {
                    Console.WriteLine("a");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_A);
                    continue;
                }
                if (ch == 'S')
                {
                    Console.WriteLine("s");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_S);
                    continue;
                }
                if (ch == 'D')
                {
                    Console.WriteLine("d");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_D);
                    continue;
                }
                if (ch == 'F')
                {
                    Console.WriteLine("f");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_F);
                    continue;
                }
                if (ch == 'G')
                {
                    Console.WriteLine("g");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_G);
                    continue;
                }
                if (ch == 'H')
                {
                    Console.WriteLine("h");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_H);
                    continue;
                }
                if (ch == 'J')
                {
                    Console.WriteLine("j");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_J);
                    continue;
                }
                if (ch == 'K')
                {
                    Console.WriteLine("k");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_K);
                    continue;
                }
                
                if (ch == 'L')
                {
                    Console.WriteLine("l");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_L);
                    continue;
                }
                if (ch == 'Z')
                {
                    Console.WriteLine("z");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_Z);
                    continue;
                }
                
                if (ch == 'X')
                {
                    Console.WriteLine("x");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_X);
                    continue;
                }
                if (ch == 'C')
                {
                    Console.WriteLine("c");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_C);
                    continue;
                }
                if (ch == 'V')
                {
                    Console.WriteLine("v");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_V);
                    continue;
                }
                if (ch == 'B')
                {
                    Console.WriteLine("b");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_B);
                    continue;
                }
                if (ch == 'N')
                {
                    Console.WriteLine("n");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_N);
                    continue;
                }
                if (ch == 'M')
                {
                    Console.WriteLine("m");
                    InputSimulator.SimulateKeyPress(VirtualKeyCode.VK_M);
                    continue;
                }
                
                if (ch != '?' && ch != ';')
                {
                    if (isX)
                    { coordX += Convert.ToString(ch); }
                    else
                    { coordY += Convert.ToString(ch); }
                }
                else
                {
                    if (ch == ';')
                        isX = false;

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