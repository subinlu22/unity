import cv2
import mediapipe as mp
import socket

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.setblocking(False)
unity_address = ("127.0.0.1", 5052)

mp_hands = mp.solutions.hands
mp_draw = mp.solutions.drawing_utils

cap = cv2.VideoCapture(0)

# [손 떨림 안정화] EMA 스무딩 — 낮을수록 더 부드럽지만 반응이 느려짐 (0.1~0.9)
SMOOTH_ALPHA = 0.4
smoothed_pos = [[None, None], [None, None]]

def is_pinch(hand_landmarks):
    thumb = hand_landmarks.landmark[4]
    index = hand_landmarks.landmark[8]
    distance = ((thumb.x - index.x)**2 + (thumb.y - index.y)**2) ** 0.5
    return distance < 0.08

with mp_hands.Hands(max_num_hands=2) as hands:
    while True:
        ret, frame = cap.read()
        if not ret:
            continue

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        result = hands.process(rgb)

        # [왼손=0, 오른손=1] 항상 고정된 슬롯에 넣기
        raw_hands = [(0.0, 0.0, False), (0.0, 0.0, False)]

        if result.multi_hand_landmarks and result.multi_handedness:
            for hand, handedness in zip(result.multi_hand_landmarks, result.multi_handedness):
                mp_draw.draw_landmarks(frame, hand, mp_hands.HAND_CONNECTIONS)
                wrist = hand.landmark[0]
                x = wrist.x
                y = wrist.y
                pinch = is_pinch(hand)
                label = handedness.classification[0].label
                slot = 0 if label == "Right" else 1
                raw_hands[slot] = (x, y, pinch)

        hand_data = []
        for i in range(2):
            x, y, pinch = raw_hands[i]

            # EMA 스무딩 적용 — 손이 없으면 초기화, 있으면 이전 위치와 혼합
            if x == 0.0 and y == 0.0:
                smoothed_pos[i] = [None, None]
            else:
                if smoothed_pos[i][0] is None:
                    smoothed_pos[i] = [x, y]
                else:
                    smoothed_pos[i][0] = SMOOTH_ALPHA * x + (1 - SMOOTH_ALPHA) * smoothed_pos[i][0]
                    smoothed_pos[i][1] = SMOOTH_ALPHA * y + (1 - SMOOTH_ALPHA) * smoothed_pos[i][1]
                x, y = smoothed_pos[i][0], smoothed_pos[i][1]

            grab = 1 if pinch else 0
            hand_data.append(f"{x},{y},{grab}")

        message = "|".join(hand_data)
        try:
            sock.sendto(message.encode(), unity_address)
        except:
            pass

        cv2.imshow("Hand Sender", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

cap.release()
cv2.destroyAllWindows()