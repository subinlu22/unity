using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class HandManager : MonoBehaviour
{
    UdpClient udpClient;
    Thread receiveThread;
    string receivedData = "";

    // [첫 번째 손]
    GameObject grabbedBlock0 = null;
    Rigidbody grabbedRb0 = null;
    bool isGrabbed0 = false;
    Vector3 lastHandPos0;
    Queue<Vector3> velocityHistory0 = new Queue<Vector3>();

    // [두 번째 손]
    GameObject grabbedBlock1 = null;
    Rigidbody grabbedRb1 = null;
    bool isGrabbed1 = false;
    Vector3 lastHandPos1;
    Queue<Vector3> velocityHistory1 = new Queue<Vector3>();

    int velocityHistorySize = 5;
    float throwThreshold = 35f; // 30f → 50f, 낮으면 흔들기만 해도 던져짐

    Camera mainCam;

    public GameObject handCursor0;
    public GameObject handCursor1;

    // [리스폰 관련]
    GameObject[] blocks;
    Vector3[] spawnPositions;

    float planeMinX = -9.7f;
    float planeMaxX = 10.3f;
    float planeMinZ = -6f;
    float planeMaxZ = 14f;
    float fallY = -5f;

    void Start()
    {
        mainCam = Camera.main;
        try { udpClient?.Close(); } catch { }
        udpClient = new UdpClient(5052);
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        blocks = GameObject.FindGameObjectsWithTag("Block");
        spawnPositions = new Vector3[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            spawnPositions[i] = blocks[i].transform.position;
        }
    }

    void ReceiveData()
    {
        while (true)
        {
            try
            {
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref endPoint);
                receivedData = Encoding.UTF8.GetString(data);
            }
            catch { break; }
        }
    }

    void CheckRespawn()
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] == grabbedBlock0 || blocks[i] == grabbedBlock1)
                continue;

            Vector3 pos = blocks[i].transform.position;
            bool outOfBounds = pos.x < planeMinX || pos.x > planeMaxX ||
                               pos.z < planeMinZ || pos.z > planeMaxZ ||
                               pos.y < fallY;

            if (outOfBounds)
            {
                Rigidbody rb = blocks[i].GetComponent<Rigidbody>();
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                blocks[i].transform.position = spawnPositions[i];
            }
        }
    }

    void ProcessHand(float rawX, float rawY, bool newGrab, GameObject cursor,
                     ref GameObject grabbedBlock, ref Rigidbody grabbedRb,
                     ref bool isGrabbed, ref Vector3 lastPos,
                     Queue<Vector3> velocityHistory)
    {
        Vector3 screenPos = new Vector3(
            (1f - rawX) * Screen.width,
            (1f - rawY) * Screen.height,
            10f
        );
        Vector3 worldPos = mainCam.ScreenToWorldPoint(screenPos);
        worldPos.z = 0f;

        if (cursor != null)
            cursor.transform.position = worldPos;

        Vector3 currentVelocity = (worldPos - lastPos) / Time.deltaTime;
        lastPos = worldPos;

        if (newGrab && grabbedBlock != null)
        {
            velocityHistory.Enqueue(currentVelocity);
            if (velocityHistory.Count > velocityHistorySize)
                velocityHistory.Dequeue();

            // [스냅 던지기] 핀치 유지 중 손 빠르게 튕기면 던지기
            if (currentVelocity.magnitude > throwThreshold)
            {
                grabbedRb.useGravity = true;
                Vector3 throwDir = currentVelocity.normalized;
                float throwSpeed = Mathf.Clamp(currentVelocity.magnitude * 0.05f, 0f, 8f);
                grabbedRb.linearVelocity = throwDir * throwSpeed;
                velocityHistory.Clear();
                grabbedBlock = null;
                grabbedRb = null;
                isGrabbed = false;
                return;
            }
        }

        // [집기 시작] OverlapSphere로 근처 블록 탐색 — Raycast보다 범위가 넓어서 잘 잡힘
        if (newGrab && !isGrabbed)
        {
            velocityHistory.Clear();

            Collider[] hits = Physics.OverlapSphere(worldPos, 2.0f); // 반경 2.0f 안의 블록 탐색
            GameObject closest = null;
            float closestDist = float.MaxValue;

            foreach (Collider col in hits)
            {
                if (col.CompareTag("Block"))
                {
                    float dist = Vector3.Distance(worldPos, col.transform.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = col.gameObject;
                    }
                }
            }

            if (closest != null)
            {
                grabbedBlock = closest;
                grabbedRb = grabbedBlock.GetComponent<Rigidbody>();
            }
        }

        // [집는 중]
        if (newGrab && grabbedBlock != null)
        {
            grabbedRb.useGravity = false;
            grabbedRb.linearVelocity = Vector3.zero;
            grabbedBlock.transform.position = worldPos;
        }
        // [핀치 풀기 = 그자리에 놓기]
        else if (!newGrab && isGrabbed && grabbedBlock != null)
        {
            grabbedRb.useGravity = true;
            grabbedRb.linearVelocity = Vector3.zero;
            velocityHistory.Clear();
            grabbedBlock = null;
            grabbedRb = null;
        }

        isGrabbed = newGrab;
    }

    void Update()
    {
        if (receivedData == "") return;

        string[] hands = receivedData.Split('|');
        if (hands.Length < 2) return;

        string[] h0 = hands[0].Split(',');
        string[] h1 = hands[1].Split(',');

        if (h0.Length == 3 && h1.Length == 3)
        {
            float x0 = float.Parse(h0[0]);
            float y0 = float.Parse(h0[1]);
            bool grab0 = h0[2] == "1";

            float x1 = float.Parse(h1[0]);
            float y1 = float.Parse(h1[1]);
            bool grab1 = h1[2] == "1";

            ProcessHand(x0, y0, grab0, handCursor0, ref grabbedBlock0, ref grabbedRb0,
                        ref isGrabbed0, ref lastHandPos0, velocityHistory0);
            ProcessHand(x1, y1, grab1, handCursor1, ref grabbedBlock1, ref grabbedRb1,
                        ref isGrabbed1, ref lastHandPos1, velocityHistory1);
        }

        CheckRespawn();
    }

    void OnDestroy()
    {
        try { receiveThread?.Abort(); } catch { }
        try { udpClient?.Close(); } catch { }
    }
}