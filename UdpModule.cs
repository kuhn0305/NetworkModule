using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// UDP를 통해 전달받을 데이터 형식
/// </summary>
public class ReceiveData
{
    public string header;
    public byte[] data;

    public ReceiveData(string header, byte[] data)
    {
        this.header = header;
        this.data = data;
    }
}

/// <summary>
/// UDP 통신 모듈
/// </summary>
public class UdpModule
{
    public delegate void ReceiveMessageHandler(object message);
    /// <summary>
    /// UDP를 통해 전달받은 데이터를 처리한 이벤트
    /// </summary>
    /// <usage>
    /// udp.OnReceiveMessage += new UdpModule.ReceiveMessageHandler(Parsing);
    /// </usage>
    public event ReceiveMessageHandler OnReceiveMessage;

    private UdpClient udpClient;
    private string broadcastIP;

    private const int udpConnectionReset = -1744830452;

    private Thread receiveThread;
    private Thread receiveQueueThread;
    private Queue<ReceiveData> dataQueue;

    private readonly int headerSize = 10;

    /// <summary>
    /// UDP 모듈을 초기화시켜준다.
    /// </summary>
    /// <param name="udpBroadcastIP"></param>
    /// <param name="udpPort"></param>
    /// <usage>
    /// UdpModule udp = new UdpModule();
    /// udp.Initialize(broadcastIP, udpServerPort);
    /// </usage>
    public void Initialize(string udpBroadcastIP, int udpPort)
    {
        broadcastIP = udpBroadcastIP;

        IPEndPoint ipep = new IPEndPoint(IPAddress.Any, udpPort);
        udpClient = new UdpClient(ipep)
        {
            EnableBroadcast = true
        };

        dataQueue = new Queue<ReceiveData>();

        receiveThread = new Thread(ReceiveMessage);
        receiveThread.Start();

        receiveQueueThread = new Thread(InvokeMessageEvent);
        receiveQueueThread.Start();

        // UDP가 ICMP 메세지를 받아 수신을 정지하는 것을 막기 위한 장치 (Exception을 무시한다)
        udpClient.Client.IOControl(udpConnectionReset, new byte[] { 0, 0, 0, 0 }, null);
    }

    /// <summary>
    /// UDP 모듈을 통해 데이터를 전달한다.
    /// </summary>
    /// <param name="header">전달할 데이터의 헤더, 크기는 10 Byte다.</param>
    /// <param name="byteData">전달한 데이터</param>
    /// <param name="receiverPort">보낼 포트</param>
    /// <param name="clientIP">보낼 IP, 공백일 경우 Broadcast로 보낸다.</param>
    /// <usage>
    /// udp.SendMessage("Command", datas, 8080, "192.168.0.100");
    /// </usage>
    public void SendMessage(string header, byte[] byteData, int receiverPort, string clientIP = null)
    {
        // byteHeaderData에 10만큼 크기를 할당한 후, byte로 변환된 string header를 넣어준다.
        byte[] headerData = new byte[headerSize];
        byte[] convertedHeaderData = Encoding.UTF8.GetBytes(header);

        for (int index = 0; index < convertedHeaderData.Length; index++)
        {
            headerData[index] = convertedHeaderData[index];
        }

        Console.WriteLine(Encoding.Default.GetString(byteData));

        // Header와 SendData를 List를 활용하여 합친다.
        var sendDataList = new List<byte>();
        sendDataList.AddRange(headerData);
        sendDataList.AddRange(byteData);

        byte[] mergedSendData = sendDataList.ToArray();

        if (clientIP == null)
            clientIP = broadcastIP;

        udpClient.Send(mergedSendData, mergedSendData.Length, clientIP, receiverPort);
    }

    public void ReceiveMessage()
    {
        try
        {
            byte[] receivedData = null;
            byte[] headerData = new byte[headerSize];
            byte[] contentsData = null;

            IPEndPoint epRemote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                receivedData = udpClient.Receive(ref epRemote);

                contentsData = new byte[receivedData.Length - headerData.Length];

                for (int index = 0; index < receivedData.Length; index++)
                {
                    if (index < headerData.Length)
                    {
                        headerData[index] = receivedData[index];
                    }
                    else
                    {
                        contentsData[index - headerData.Length] = receivedData[index];
                    }
                }

                string udpMessage = Encoding.Default.GetString(receivedData);

                ReceiveData receiveData = new ReceiveData(Encoding.Default.GetString(headerData), contentsData);

                dataQueue.Clear();
                dataQueue.Enqueue(receiveData);
            }
        }
        catch
        {

        }
    }

    private async void InvokeMessageEvent()
    {
        try
        {
            while (true)
            {
                if (dataQueue.Count > 0)
                {
                     OnReceiveMessage.Invoke(dataQueue.Dequeue());
                }

                await Task.Delay(30);
            }
        }
        catch
        {

        }
    }

    public void Terminate()
    {
        receiveThread = null;

        udpClient.Close();
    }
}
