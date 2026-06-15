using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class BlockController : MonoBehaviour
{
    UdpClient udpClient;
    Thread receiveThread;
    string receivedData = "";

    bool isGrabbed = false;
    Rigidbody rb;

    Queue<Vector3> positionHistory = new Queue<Vector3>();
    int historySize = 5;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        udpClient = new UdpClient(5052);
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void ReceiveData()
    {
        while (true)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = udpClient.Receive(ref endPoint);
            receivedData = Encoding.UTF8.GetString(data);
        }
    }

    void Update()
    {
        if (receivedData != "")
        {
            string[] values = receivedData.Split(',');
            if (values.Length == 3)
            {
                float newX = float.Parse(values[0]);
                float newY = float.Parse(values[1]);
                bool newGrab = values[2] == "1";

                float posX = (newX - 0.5f) * 10f;
                float posY = (0.5f - newY) * 10f;
                Vector3 targetPos = new Vector3(posX, posY, 0f);

                if (newGrab)
                {
                    rb.useGravity = false;
                    rb.linearVelocity = Vector3.zero;
                    transform.position = targetPos;

                    // 위치 기록
                    positionHistory.Enqueue(targetPos);
                    if (positionHistory.Count > historySize)
                        positionHistory.Dequeue();
                }
                else if (isGrabbed && !newGrab)
                {
                    // 놓을 때 → 히스토리로 속도 계산
                    rb.useGravity = true;
                    if (positionHistory.Count >= 2)
                    {
                        Vector3[] arr = positionHistory.ToArray();
                        Vector3 throwVelocity = (arr[arr.Length - 1] - arr[0]) / (Time.deltaTime * historySize);
                        rb.linearVelocity = throwVelocity * 1.5f;
                    }
                    positionHistory.Clear();
                }

                isGrabbed = newGrab;
            }
        }
    }

    void OnDestroy()
    {
        receiveThread.Abort();
        udpClient.Close();
    }
}