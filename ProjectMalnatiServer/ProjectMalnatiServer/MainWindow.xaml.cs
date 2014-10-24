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
            //wewe
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
                myEP = new IPEndPoint(IPAddress.Parse(this.textBoxIP.Text), Convert.ToInt16(this.textBoxPort.Text));
                this.buttonListen.Content = "Cancel";
                this.listeningTextBlock.Text = "Listening...";
                this.textBoxIP.IsEnabled = false;
                this.textBoxPort.IsEnabled = false;
                this.textBoxPassword.IsEnabled = false;
                this.pass = this.textBoxPassword.Text;
                workerThreadConnection = new Thread(connetti);
                workerThreadConnection.Start();
            }
            else
                // else if(this.connesso == true && this.buttonListen.Content == "Cancel")
                disconnetti();
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
                workerThread.Join();
            }
            if (acceptedSocket != null)
            {
                acceptedSocket.Shutdown(SocketShutdown.Both);
                //acceptedSocket.Close(); //senno' non posso riciclare il socket
                acceptedSocket.Disconnect(true);
            }
            this.connesso = false;
            this.textBoxIP.IsEnabled = true;
            this.textBoxPort.IsEnabled = true;
            this.textBoxPassword.IsEnabled = true;
            this.buttonListen.IsEnabled = true;
            this.buttonListen.Content = "Listen";
            this.listeningTextBlock.Text = "";
            _shouldStop = false; //nel caso non si tratti di chiusura dell'applicazione
        }

        private void connetti()
        {
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket handler = null;

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
                                acceptedSocket.ReceiveTimeout = 30000;
                                break;
                            }
                            else
                            {
                                handler.Send(Encoding.UTF8.GetBytes("N"));
                                handler.Shutdown(SocketShutdown.Both);
                                handler.Disconnect(true); //chiude la connessione ma lascia il socket riutilizzabile
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

        //private void parseFunction(string bufferString)
        //{
        //    foreach (char ch in bufferString)
        //    {
        //        if (ch == '\0')
        //            return;

        //        if (ch == 'U')
        //        {
        //            Win32.mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)p.x, (uint)p.y, 0, 0);
        //            continue;
        //        }

        //        if (ch == 'D')
        //        {
        //            Win32.mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)p.x, (uint)p.y, 0, 0);
        //            continue;
        //        }

        //        if (ch == 'R')
        //        {
        //            Win32.mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, (uint)p.x, (uint)p.y, 0, 0);
        //            continue;
        //        }

        //        if (ch != '?' && ch != ';')
        //        {
        //            if (isX)
        //            { coordX += Convert.ToString(ch); }
        //            else
        //            { coordY += Convert.ToString(ch); }
        //        }
        //        else
        //        {
        //            if (ch == ';')
        //                isX = false;

        //            else if (ch == '?')
        //            {
        //                isX = true;
        //                if (coordX != "" && coordY != "")
        //                {
        //                    double x_rel = Convert.ToDouble(coordX);
        //                    double y_rel = Convert.ToDouble(coordY);
        //                    //p.x = Convert.ToInt16(x_rel * System.Windows.SystemParameters.PrimaryScreenWidth);
        //                    //p.y = Convert.ToInt16(y_rel * System.Windows.SystemParameters.PrimaryScreenHeight);

        //                    p.x = Convert.ToInt16(x_rel * 1920);
        //                    p.y = Convert.ToInt16(y_rel * 1200);

        //                    coordX = "";
        //                    coordY = "";

        //                    //Win32.ClientToScreen(this.Handle, ref p);
        //                    //Win32.ClientToScreen(hWnd, ref p);

        //                    Console.WriteLine("x:" + p.x + " y:" + p.y + "\n");

        //                    Win32.SetCursorPos(p.x, p.y);
        //                }
        //            }
        //        }
        //    }
        //}

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

        public void MyReceive()
        {
            while (!_shouldStop)
            {
                try
                {
                    byte[] buffer = new byte[50];
                    string bufferString;

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
                    {
                        disconnetti();
                        MessageBox.Show("Connessione con il client caduta");
                        Action showWindow = () => { this.Show(); };
                        dispatcher.Invoke(showWindow);
                    }
                }
            }
            Console.WriteLine("Il thread myReceive sta per terminare\n");
        }

        private void closeWindowOperations(object sender, System.ComponentModel.CancelEventArgs e)
        {
            disconnetti();
            if (acceptedSocket != null)
                acceptedSocket.Close(); //se sto chiudendo l'applicazione il socket va effettivamente chiuso
        }
    }

}