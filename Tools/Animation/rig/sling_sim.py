def simulate_sling():
    """The pouch is a weight on a rope from the right hand (never longer than the sling, slack
    allowed), simulated with Verlet over a few cycles so the recorded cycle loops. Returns per
    frame a polyline from the grip to the pouch: straight when taut, sagging when slack."""
    sub = 60
    dt = 1.0 / (FPS * sub)
    _, pts = pose(0.0)
    stone = pts["handR"] + np.array([0, -SLING_LEN, 0])
    prev = stone.copy()
    record = []
    cycles = 3
    for frame_index in range(FRAMES * cycles):
        for s in range(sub):
            t = (frame_index * sub + s) * dt
            _, pts = pose(t)
            hand = pts["handR"]
            vel = (stone - prev) * 0.997
            prev = stone.copy()
            stone = stone + vel + GRAVITY * dt * dt
            d = stone - hand
            L = np.linalg.norm(d)
            if L > SLING_LEN:
                stone = hand + d / L * SLING_LEN
            pin = pinned(t)
            if pin > 0:
                target = pts["handL"] + np.array([0.0, -0.03, 0.0])
                stone = stone + (target - stone) * pin
        if frame_index >= FRAMES * (cycles - 1):
            record.append(rope(hand, stone))
    return record


def rope(hand, end, n=SLING_N):
    d = end - hand
    L = np.linalg.norm(d)
    slack = np.sqrt(max(0.0, SLING_LEN ** 2 - L ** 2)) * 0.5
    s = np.linspace(0, 1, n + 1)[:, None]
    return hand + d * s + np.array([0, -1, 0]) * slack * 4 * s * (1 - s)
