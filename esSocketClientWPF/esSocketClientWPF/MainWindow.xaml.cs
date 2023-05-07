using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.Timers;
using System.Windows.Threading;

namespace esSocketClientWPF
{
    /// <summary>
    /// Funzionamento generale
    /// Gestione Canvas -->
    /// Nell'interfaccia ci sono degli InkCanvas che hanno la proprietà Strokes di tipo StrokeCollection che contiene tutti i tratti disegnati
    /// StrokeCollection può essere salvata su file con il formato ISF(Ink Serialized Format, dovrebbe essere base64)
    /// Per modificare il contenuto del InkCanvas si può assegnare a Strokes un altro StrokeCollection
    /// Quindi quello che faccio io è
    /// Per trasformare da InkCanvas a byte[] faccio canvInvio.Strokes.Save(fs, true), poi leggo il file sotto forma di bytes (File.ReadAllBytes(inkFileName)) e i byte sono già pronti per essere inviati
    /// Per trasformare da byte[] a InkCanvas scrivo prima su file i byte arrivati (File.WriteAllBytes(inkFileName, dati)) e poi passo il file al costruttore di StrokeCollection (StrokeCollection strokes = new StrokeCollection(fs);) e lo assegno a inkCanvas.Strokes
    ///
    /// Gestione socket-->
    /// Send è un semplice socket.Send(byte della conversione canvas)
    /// Il Receive essendo che deve essere costantemente in ascolto è un while(true) dentro un Task che legge il buffer quando c'è qualcosa
    /// </summary>
    public partial class MainWindow : Window
    {
        DispatcherTimer timer;
        int currTime;
        const int durataSecondi = 90;
        //per gli oggetti da disegnare si può fare un file con migliaia di oggetti che viene letto all'inizio ma non mi sembra necessario al momento
        readonly string[] oggettiDaDisegnare = { "bicchiere", "moneta", "braccialetto", "maglia", "macchina", "albero" };
        string daDisegnare;
        string inkFileName = @"canvas.txt";
        Socket socket;
        Socket socketText;
        bool isSending = false, isReceiving = false;
        const int MAX_BUFFER = 1 << 15;
        object _lock;
        bool myTurn;
        
        public MainWindow()
        {
            _lock = new object();
            InitializeComponent();
            cp.SelectedColor = Color.FromRgb(0, 0, 0);
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += TickEvent;
            myTurn = false;
            canvInvio.IsEnabled = false;
        }

        private byte[] CanvasToBytes()
        {
            try
            {
                using (FileStream fs = new FileStream(inkFileName, FileMode.OpenOrCreate))
                {
                    canvInvio.Strokes.Save(fs, true);
                }

                return File.ReadAllBytes(inkFileName);
            }
            catch (Exception) { }

            return new byte[0];
        }

        private StrokeCollection BytesToCanvas(byte[] dati)
        {
            try
            {
                File.WriteAllBytes(inkFileName, dati);

                using (FileStream fs = new FileStream(inkFileName, FileMode.Open))
                {
                    StrokeCollection strokes = new StrokeCollection(fs);
                    return strokes;
                }
            }
            catch (Exception) { }


            return new StrokeCollection();
        }

        #region Gestione socket
        private void StartServer()
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry("localhost"); //ip di host, 127.0.0.1
                IPAddress ipHost = host.AddressList[0]; //ritorna un array ma in teoria c'è ne è solo uno

                IPEndPoint valoriSocket = new IPEndPoint(ipHost, 11000); //associa ip-porta
                IPEndPoint text = new IPEndPoint(ipHost, 10500);
                //genera socket
                socket = new Socket(ipHost.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socketText = new Socket(ipHost.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                socket.Connect(valoriSocket);
                socketText.Connect(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore: " + ex.Message);
            }
        }
        private async Task LabelColor()
        {
            while (true)
            {
                lbl_disegna.Foreground = PickRandomBrush();
                lbl_timer.Foreground = PickRandomBrush();
                await Task.Delay(500);
            }
        }
        private Brush PickRandomBrush()
        {
            Random r = new Random();
            return new SolidColorBrush(Color.FromArgb(255, (byte)r.Next(0, 200), (byte)r.Next(0, 200), (byte)r.Next(0, 200)));
        }

        private async Task RiceviMessaggio()
        {
            string messaggio = null;
            byte[] dati = null;

            while (true)
            {
                while (socketText.Available <= 0) await Task.Delay(10);
                dati = new byte[1 << 10];

                int lenBytes = socketText.Receive(dati);
                
                messaggio += Encoding.ASCII.GetString(dati, 0, lenBytes);
                if (messaggio.Contains("<SUB>"))
                {
                    string res = messaggio.Substring(5, messaggio.Length - 5);
                    if (res.ToLower() == daDisegnare.ToLower())
                    {
                        SendText("<RES>1");
                        lst_messaggi.Items.Add("OTHER HA INDOVINATO!");
                        FineTurno();
                    }
                    else
                    {
                        SendText("<RES>0");
                    }
                }
                else if (messaggio.Contains("<RES>"))
                {
                    if (messaggio.Substring(5, messaggio.Length - 5) == "1")
                    {
                        lst_messaggi.Items.Add("HAI INDOVINATO!");
                        AvviaTurno();
                    }
                    else
                        MessageBox.Show("SBAGLIATO!", "ERRORE", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    lst_messaggi.Items.Add("Other: " + messaggio);
                }
                messaggio = null;
            }
        }
        private void StartTimer()
        {
            currTime = durataSecondi;
            lbl_timer.Content = null;
            lbl_timer.Content = currTime;
            timer.Start();
        }
        private void StopTimer()
        {
            timer.Stop();
        }
        private void TickEvent(object sender, EventArgs e)
        {
            currTime--;
            lbl_timer.Content = null;
            lbl_timer.Content = currTime;
            if(currTime == 0)
            {
                lst_messaggi.Items.Add("TEMPO SCADUTO! INVERSIONE DI RUOLI");
                if (lbl_disegna.Content.ToString() == "INDOVINA")
                    AvviaTurno();
                else
                    FineTurno();
            }
        }
        private void FineTurno()
        {
            myTurn = false;
            lbl_disegna.Content = null;
            lbl_disegna.Content = "INDOVINA";
            canvInvio.IsEnabled = false;
            txt_guess.IsEnabled = true;
            btn_submit.IsEnabled = true;
            StopTimer();
            StartTimer();
            canvInvio.Strokes.Clear();
        }
        private void AvviaTurno()
        {
            myTurn = true;
            daDisegnare = oggettiDaDisegnare[new Random().Next(0, oggettiDaDisegnare.Length)];
            lbl_disegna.Content = null;
            lbl_disegna.Content = "Disegna: " + daDisegnare;
            canvInvio.IsEnabled = true;
            txt_guess.IsEnabled = false;
            btn_submit.IsEnabled = false;
            StopTimer();
            StartTimer();
            canvInvio.Strokes.Clear();
        }

        private async Task SendCanvas()
        {
            while (true)
            {
                while (isReceiving || !myTurn) await Task.Delay(50);
                isSending = true;
                byte[] messaggio = CanvasToBytes();
                if (messaggio.Length > MAX_BUFFER) throw new Exception("Canvas supera la dimensione massima consentita");
                int byteSent = socket.Send(messaggio);
                isSending = false;
                await Task.Delay(150);
            }
        }
        private async Task RiceviCanvas()
        {
            try
            {
                byte[] dati;

                while (true)
                {
                    while (isSending || socket.Available <= 0 || myTurn)
                    {
                        await Task.Delay(50);//lascia l'interfaccia fare le sue cose e poi riesegue, senza Task rimarrebbe bloccato all'infinito
                    }
                    dati = new byte[MAX_BUFFER];

                    isReceiving = true;
                    int lenBytes = socket.Receive(dati);
                    byte[] toConvert = new byte[1];
                    bool esci = false;

                    //essendo che non so quanto è lungo il messaggio leggo il buffer al contrario e salto la fila di 0 alla fine, dal primo non 0 inizio a salvare i byte
                    for (int i = lenBytes - 1; i >= 0 && !esci; i--)//un po' brutto però funziona
                    {
                        if (dati[i] != 0)//gli ultimi sono tutti 0
                        {
                            toConvert = new byte[i + 1];
                            for (int j = 0; j <= i; j++)
                            {
                                toConvert[j] = dati[j];
                            }

                            esci = true;
                        }
                    }

                    canvInvio.Strokes = BytesToCanvas(toConvert);
                    isReceiving = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SendText(string text)
        {
            lock (_lock)
            {
                Thread.Sleep(15);
                //spedisco i dati e ricevo la risposta
                int bytesSent = socketText.Send(Encoding.ASCII.GetBytes(text));
            }
        }
        #endregion


        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartServer();
                btnStart.IsEnabled = false;
                lbl_disegna.Content = null;
                lbl_disegna.Content = "INDOVINA";
                LabelColor();
                SendCanvas();
                RiceviMessaggio();
                RiceviCanvas();
                StartTimer();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void btnInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("ATTENZIONE: cliccare start sul server e in seguito sul client, l'interfaccia rimane bloccata finche non viene eseguito\n" +
                "Sul canvas sinistro scrivere il disegno da inviare mentre sul destro si riceve\n" +
                "Tramite carica file su può caricare un Canvas precedentemente salvato");
        }

        private void cp_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            canvInvio.DefaultDrawingAttributes.Color = (Color)cp.SelectedColor;
        }

        private void btn_gomma_Click(object sender, RoutedEventArgs e)
        {
            canvInvio.DefaultDrawingAttributes.Color = ((SolidColorBrush)canvInvio.Background).Color;
            cp.SelectedColor = ((SolidColorBrush)canvInvio.Background).Color;
        }


        private void sld_size_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            canvInvio.DefaultDrawingAttributes.Width = sld_size.Value;
            canvInvio.DefaultDrawingAttributes.Height = sld_size.Value;
        }

        private void btn_submit_Click(object sender, RoutedEventArgs e)
        {
            SendText("<SUB>"+txt_guess.Text);
            txt_guess.Text = null;
        }

        private void btn_send_Click(object sender, RoutedEventArgs e)
        {
            SendText(txt_invia.Text);
            lst_messaggi.Items.Add("You: " + txt_invia.Text);
            txt_invia.Text = null;
        }
    }
}
