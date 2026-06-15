using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class HandManager : MonoBehaviour
{
    // ── 소켓 ────────────────────────────────────────
    UdpClient udpClient;
    Thread receiveThread;
    volatile string receivedData = "";

    // ── 손 0 ────────────────────────────────────────
    GameObject grabbedBlock0;
    Rigidbody grabbedRb0;
    bool isGrabbed0;
    bool prevGrab0;
    Vector3 smoothPos0;
    Vector3 lastRawPos0;
    Queue<Vector3> velHistory0 = new Queue<Vector3>();
    int releaseCounter0;

    // ── 손 1 ────────────────────────────────────────
    GameObject grabbedBlock1;
    Rigidbody grabbedRb1;
    bool isGrabbed1;
    bool prevGrab1;
    Vector3 smoothPos1;
    Vector3 lastRawPos1;
    Queue<Vector3> velHistory1 = new Queue<Vector3>();
    int releaseCounter1;

    // ── 파라미터 ─────────────────────────────────────
    float grabRadius     = 1.0f;   // 집기 감지 반경
    float smoothAlpha    = 0.6f;   // 커서 스무딩
    float throwThreshold = 60f;      // 보유 중 최대 속도가 이 이상이면 던지기
    int   releaseFrames  = 3;        // 핀치 풀린 후 이 프레임 수 유지돼야 진짜 놓기
    float throwMultiplier = 50f;  // 던지기 속도 배율
    float throwMaxSpeed  = 12f;    // 던지기 최대 속도
    int   velHistorySize = 8;      // 속도 기록 프레임 수
    float cameraDistance = 10f;

    // ── 씬 ──────────────────────────────────────────
    Camera mainCam;
    public GameObject handCursor0;
    public GameObject handCursor1;

    // ── 리스폰 ──────────────────────────────────────
    GameObject[] blocks;
    Vector3[] spawnPositions;
    float planeMinX = -9.7f, planeMaxX = 10.3f;
    float planeMinZ = -6f,   planeMaxZ = 14f;
    float fallY = -5f;

    // ────────────────────────────────────────────────
    void Start()
    {
        mainCam = Camera.main;
        udpClient = new UdpClient(5052);
        receiveThread = new Thread(ReceiveData) { IsBackground = true };
        receiveThread.Start();

        blocks = GameObject.FindGameObjectsWithTag("Block");
        spawnPositions = new Vector3[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
            spawnPositions[i] = blocks[i].transform.position;
    }

    void ReceiveData()
    {
        while (true)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                receivedData = Encoding.UTF8.GetString(udpClient.Receive(ref ep));
            }
            catch { break; }
        }
    }

    // ────────────────────────────────────────────────
    void Update()
    {
        string data = receivedData;
        if (data == "") return;

        string[] hands = data.Split('|');
        if (hands.Length < 2) return;

        string[] h0 = hands[0].Split(',');
        string[] h1 = hands[1].Split(',');
        if (h0.Length != 3 || h1.Length != 3) return;

        if (!float.TryParse(h0[0], out float x0) || !float.TryParse(h0[1], out float y0)) return;
        if (!float.TryParse(h1[0], out float x1) || !float.TryParse(h1[1], out float y1)) return;
        bool grab0 = h0[2] == "1";
        bool grab1 = h1[2] == "1";

        ProcessHand(x0, y0, grab0, handCursor0,
                    ref grabbedBlock0, ref grabbedRb0,
                    ref isGrabbed0, ref prevGrab0,
                    ref smoothPos0, ref lastRawPos0, velHistory0,
                    ref releaseCounter0);

        ProcessHand(x1, y1, grab1, handCursor1,
                    ref grabbedBlock1, ref grabbedRb1,
                    ref isGrabbed1, ref prevGrab1,
                    ref smoothPos1, ref lastRawPos1, velHistory1,
                    ref releaseCounter1);

        CheckRespawn();
    }

    void ProcessHand(float rawX, float rawY, bool grab, GameObject cursor,
                     ref GameObject block, ref Rigidbody rb,
                     ref bool isGrabbed, ref bool prevGrab,
                     ref Vector3 smoothPos, ref Vector3 lastRawPos,
                     Queue<Vector3> velHistory, ref int releaseCounter)
    {
        bool active = !(rawX == 0f && rawY == 0f);

        if (!active)
        {
            if (cursor != null) cursor.SetActive(false);
            Release(ref block, ref rb, ref isGrabbed, ref prevGrab,
                    ref smoothPos, ref lastRawPos, velHistory, Vector3.zero);
            return;
        }

        // 좌표 변환
        Vector3 worldPos = mainCam.ScreenToWorldPoint(new Vector3(
            (1f - rawX) * Screen.width,
            (1f - rawY) * Screen.height,
            cameraDistance));
        worldPos.z = 0f;

        // 스무딩 (smoothPos 전용)
        if (smoothPos == Vector3.zero) smoothPos = worldPos;
        smoothPos = Vector3.Lerp(smoothPos, worldPos, smoothAlpha);

        if (cursor != null)
        {
            cursor.SetActive(true);
            cursor.transform.position = smoothPos;
        }

        // 속도 계산 — UDP 패킷이 실제로 바뀐 프레임에서만 유효
        float dt = Mathf.Max(Time.deltaTime, 0.001f);
        bool posChanged = lastRawPos != Vector3.zero
                          && (worldPos - lastRawPos).sqrMagnitude > 1e-8f;
        Vector3 vel = posChanged ? (worldPos - lastRawPos) / dt : Vector3.zero;
        if (lastRawPos == Vector3.zero || posChanged) lastRawPos = worldPos;

        // ── 집기 ─────────────────────────────────────
        if (grab && !prevGrab)
        {
            velHistory.Clear();
            lastRawPos = worldPos;
            releaseCounter = 0;
            // XY 거리 < grabRadius AND 콜라이더 XY 범위 안 — 둘 다 만족할 때만 집기
            GameObject closest = null;
            float minDist = float.MaxValue;
            foreach (GameObject b in blocks)
            {
                float d = Vector2.Distance(
                    new Vector2(smoothPos.x, smoothPos.y),
                    new Vector2(b.transform.position.x, b.transform.position.y));
                if (d >= grabRadius || d >= minDist) continue;
                Collider col = b.GetComponent<Collider>();
                if (col == null) continue;
                Bounds bounds = col.bounds;
                if (smoothPos.x < bounds.min.x || smoothPos.x > bounds.max.x) continue;
                if (smoothPos.y < bounds.min.y || smoothPos.y > bounds.max.y) continue;
                minDist = d; closest = b;
            }
            if (closest != null)
            {
                block = closest;
                rb = block.GetComponent<Rigidbody>();
                isGrabbed = true;
            }
        }

        // ── 이동 (속도 기록) ─────────────────────────
        if (isGrabbed && block != null)
        {
            if (grab)
            {
                rb.isKinematic = true;
                block.transform.position = smoothPos;
                releaseCounter = 0;
            }

            // 핀치 풀린 직후 3프레임도 속도 기록 (던지기 팔로우스루 포함)
            if (posChanged)
            {
                velHistory.Enqueue(vel);
                if (velHistory.Count > velHistorySize) velHistory.Dequeue();
            }
        }

        // ── 놓기 대기 / 던지기 판정 ──────────────────
        if (!grab && isGrabbed && block != null)
        {
            releaseCounter++;
            if (releaseCounter >= releaseFrames)
            {
                Vector3 bestVel = Vector3.zero;
                foreach (Vector3 v in velHistory)
                    if (v.magnitude > bestVel.magnitude) bestVel = v;

                Vector3 throwVel = Vector3.zero;
                if (bestVel.magnitude >= throwThreshold)
                {
                    float speed = Mathf.Min(bestVel.magnitude * throwMultiplier, throwMaxSpeed);
                    throwVel = bestVel.normalized * speed;
                }

                Release(ref block, ref rb, ref isGrabbed, ref prevGrab,
                        ref smoothPos, ref lastRawPos, velHistory, throwVel);
                releaseCounter = 0;
                return;
            }
        }

        prevGrab = grab;
    }

    void Release(ref GameObject block, ref Rigidbody rb,
                 ref bool isGrabbed, ref bool prevGrab,
                 ref Vector3 smoothPos, ref Vector3 lastRawPos,
                 Queue<Vector3> velHistory, Vector3 throwVel)
    {
        if (block != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = throwVel;
        }
        block = null; rb = null;
        isGrabbed = false; prevGrab = false;
        smoothPos = Vector3.zero; lastRawPos = Vector3.zero;
        velHistory.Clear();
    }

    void CheckRespawn()
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] == grabbedBlock0 || blocks[i] == grabbedBlock1) continue;
            Vector3 p = blocks[i].transform.position;
            if (p.x < planeMinX || p.x > planeMaxX ||
                p.z < planeMinZ || p.z > planeMaxZ || p.y < fallY)
            {
                var rb = blocks[i].GetComponent<Rigidbody>();
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                blocks[i].transform.position = spawnPositions[i];
            }
        }
    }

    void OnDestroy()
    {
        try { receiveThread?.Abort(); } catch { }
        try { udpClient?.Close(); } catch { }
    }
}