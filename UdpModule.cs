using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// UDP 통신 모듈
/// </summary>
public class UdpModule
{
    /// <summary>
    /// UDP를 통해 전달받을 데이터 형식
    /// </summary>
    public class ReceiveData
    {
        public string header;
        public byte[] content;

        public ReceiveData(string header, byte[] content)
        {
            this.header = header;
            this.content = content;
        }
    }

    public delegate void ReceiveMessageHandler(ReceiveData message);
    /// <summary>
    /// UDP를 통해 전달받은 데이터를 처리한 이벤트
    /// </summary>
    /// <usage>
    /// udp.OnReceiveMessage += ShowLog;
    /// 
    /// private void ShowLog(UdpModule.ReceiveData message)
    /// {
    ///     Console.WriteLine($"{message.header} : {Encoding.Default.GetString(message.content)}");
    /// }
    /// </usage>
    public event ReceiveMessageHandler OnReceiveMessage;

    private UdpClient udpClient;
    private Thread receiveThread;
    private Thread invokeMessageEventThread;

    private Queue<ReceiveData> receiveDataQueue;

    private readonly int udpConnectionReset = -1744830452;
    private readonly int headerSize = 10;
    private string broadcastIP;

    /// <summary>
    /// UDP 모듈을 초기화시켜준다.
    /// </summary>
    /// <param name="broadcastIP">Broadcast로 보낼 IP</param>
    /// <param name="port">열어놓을 포트</param>
    /// <usage>
    /// UdpModule udp = new UdpModule();
    /// udp.Initialize(broadcastIP, udpServerPort);
    /// </usage>
    public void Initialize(string broadcastIP, int port)
    {
        this.broadcastIP = broadcastIP;

        IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Any, port);
        udpClient = new UdpClient(iPEndPoint)
        {
            EnableBroadcast = true
        };

        receiveDataQueue = new Queue<ReceiveData>();

        receiveThread = new Thread(ReceiveMessage);
        receiveThread.Start();

        invokeMessageEventThread = new Thread(InvokeMessageEvent);
        invokeMessageEventThread.Start();

        // UDP가 ICMP 메세지를 받아 수신을 정지하는 것을 막기 위한 장치 (Exception을 무시한다)
        //udpClient.Client.IOControl(udpConnectionReset, new byte[] { 0, 0, 0, 0 }, null);
    }
    /// <summary>
    /// UDP 모듈을 통해 데이터를 전달한다.
    /// </summary>
    /// <param name="header">전달할 데이터의 헤더, 크기는 10 Byte까지다.</param>
    /// <param name="contentsData">전달할 데이터</param>
    /// <param name="targetPort">전달할 대상의 포트</param>
    /// <param name="targetIP">전달할 대상의 IP, 공백일 경우 Broadcast로 보낸다.</param>
    /// <usage>
    /// udp.SendMessage("Command", datas, 8080, "192.168.0.100");
    /// </usage>
    public void SendMessage(string header, byte[] contentsData, int targetPort, string targetIP = null)
    {
        byte[] headerData = Encoding.UTF8.GetBytes(header);
        Array.Resize(ref headerData, headerSize);

        byte[] sendData = new byte[headerData.Length + contentsData.Length];
        Array.Copy(headerData, sendData, headerData.Length);
        Array.Copy(contentsData, 0, sendData, headerData.Length, contentsData.Length);

        if (targetIP == null)
        {
            targetIP = broadcastIP;
        }

        Console.WriteLine(Encoding.Default.GetString(sendData));

        udpClient.Send(sendData, sendData.Length, targetIP, targetPort);
    }
    public void Terminate()
    {
        receiveThread.Abort();
        invokeMessageEventThread.Abort();

        udpClient.Close();
    }

    private void ReceiveMessage()
    {
        try
        {
            byte[] receivedData = null;
            byte[] headerData = new byte[headerSize];
            byte[] contentsData = null;

            IPEndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                receivedData = udpClient.Receive(ref senderEndPoint);
                contentsData = new byte[receivedData.Length - headerData.Length];

                Array.Copy(receivedData, headerData, headerData.Length);
                headerData = Array.FindAll(headerData, o => o != 0);
                Array.Copy(receivedData, headerData.Length, contentsData, 0, contentsData.Length);

                ReceiveData receiveData = new ReceiveData(Encoding.Default.GetString(headerData), contentsData);

                receiveDataQueue.Enqueue(receiveData);
            }
        }
        catch (ThreadInterruptedException e)
        {

        }
    }
    private void InvokeMessageEvent()
    {
        try
        {
            while (true)
            {
                if (receiveDataQueue.Count > 0)
                {
                    OnReceiveMessage.Invoke(receiveDataQueue.Dequeue());
                }
            }
        }
        catch (ThreadInterruptedException e)
        {

        }
    }
}
