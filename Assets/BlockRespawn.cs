using UnityEngine;

// =============================================
// [BlockRespawn]
// RespawnZone 태그 오브젝트에 닿으면 시작 위치로 복귀
// 각 블록에 이 스크립트를 붙여서 사용
// =============================================
public class BlockRespawn : MonoBehaviour
{
    // 시작 위치 저장 (게임 시작할 때 자동으로 저장됨)
    Vector3 spawnPosition;
    Rigidbody rb;

    void Start()
    {
        // 게임 시작할 때 현재 위치를 시작 위치로 저장
        spawnPosition = transform.position;
        rb = GetComponent<Rigidbody>();
    }

    // OnCollisionEnter = 다른 오브젝트와 충돌하는 순간 자동 실행
    void OnCollisionEnter(Collision collision)
    {
        // 충돌한 오브젝트가 "Respawn" 태그면 리스폰
        if (collision.gameObject.CompareTag("Respawn"))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.position = spawnPosition;
        }
    }
}
