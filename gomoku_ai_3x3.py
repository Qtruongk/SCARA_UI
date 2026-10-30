import os
import matplotlib.pyplot as plt

SIZE = 3
WIN_LENGTH = 3
EMPTY = "."
HUMAN = "X"
ROBOT = "O"
MAX_DEPTH = SIZE * SIZE
WIN_SCORE = 1_000_000

# Đường dẫn lưu file theo yêu cầu của bạn
SAVE_DIR = r"D:\Scara_Caro"

DIRECTIONS = [
    (0, 1),    # Ngang
    (1, 0),    # Doc
    (1, 1),    # Cheo xuong
    (1, -1),   # Cheo len
]


def create_board():
    return [[EMPTY for _ in range(SIZE)] for _ in range(SIZE)]


def print_board(board):
    print("\n    " + "   ".join(str(c) for c in range(SIZE)))
    print("  +" + "----" * SIZE + "+")

    for r in range(SIZE):
        print(f"{r} | " + " | ".join(board[r]) + " |")
        print("  +" + "----" * SIZE + "+")

    print()


def is_valid_move(board, row, col):
    return (
        0 <= row < SIZE
        and 0 <= col < SIZE
        and board[row][col] == EMPTY
    )


def is_full(board):
    return all(cell != EMPTY for row in board for cell in row)


def get_winner(board):
    """Tra ve X/O neu co 3 quan lien tiep."""
    for r in range(SIZE):
        for c in range(SIZE):
            player = board[r][c]

            if player == EMPTY:
                continue

            for dr, dc in DIRECTIONS:
                end_r = r + (WIN_LENGTH - 1) * dr
                end_c = c + (WIN_LENGTH - 1) * dc

                if not (
                    0 <= end_r < SIZE
                    and 0 <= end_c < SIZE
                ):
                    continue

                won = True

                for k in range(1, WIN_LENGTH):
                    nr = r + k * dr
                    nc = c + k * dc

                    if board[nr][nc] != player:
                        won = False
                        break

                if won:
                    return player

    return None


def has_winning_move(board, player, row, col):
    """Kiem tra nuoc vua danh co tao thanh 3 quan lien tiep."""
    for dr, dc in DIRECTIONS:
        count = 1

        for sign in (1, -1):
            nr = row + sign * dr
            nc = col + sign * dc

            while (
                0 <= nr < SIZE
                and 0 <= nc < SIZE
                and board[nr][nc] == player
            ):
                count += 1
                nr += sign * dr
                nc += sign * dc

        if count >= WIN_LENGTH:
            return True

    return False


def get_candidate_moves(board):
    """Lay cac o trong gan cac quan da danh."""
    occupied = [
        (r, c)
        for r in range(SIZE)
        for c in range(SIZE)
        if board[r][c] != EMPTY
    ]

    if not occupied:
        return [(SIZE // 2, SIZE // 2)]

    candidates = set()

    for r, c in occupied:
        for dr in range(-2, 3):
            for dc in range(-2, 3):
                nr = r + dr
                nc = c + dc

                if (
                    0 <= nr < SIZE
                    and 0 <= nc < SIZE
                    and board[nr][nc] == EMPTY
                ):
                    candidates.add((nr, nc))

    return list(candidates)


def find_immediate_move(board, player):
    """Tim nuoc thang ngay cua player."""
    for row, col in get_candidate_moves(board):
        board[row][col] = player
        wins = has_winning_move(board, player, row, col)
        board[row][col] = EMPTY

        if wins:
            return row, col

    return None


def find_all_winning_moves(board, player):
    """Tim tat ca cac nuoc di giup player thang ngay o luot hien tai."""
    winning_moves = []
    for row, col in get_candidate_moves(board):
        board[row][col] = player
        if has_winning_move(board, player, row, col):
            winning_moves.append((row, col))
        board[row][col] = EMPTY
    return winning_moves


def evaluate_window(window):
    """Cham diem mot chuoi gom 3 o."""
    robot_count = window.count(ROBOT)
    human_count = window.count(HUMAN)

    if robot_count > 0 and human_count > 0:
        return 0

    scores = {
        0: 0,
        1: 10,
        2: 100,
        3: WIN_SCORE,
    }

    if robot_count:
        return scores[robot_count]

    if human_count:
        return -scores[human_count]

    return 0


def evaluate_board(board):
    """Danh gia cac chuoi 3 o tren toan bo ban co."""
    score = 0

    for r in range(SIZE):
        for c in range(SIZE):
            for dr, dc in DIRECTIONS:
                end_r = r + (WIN_LENGTH - 1) * dr
                end_c = c + (WIN_LENGTH - 1) * dc

                if not (
                    0 <= end_r < SIZE
                    and 0 <= end_c < SIZE
                ):
                    continue

                window = [
                    board[r + k * dr][c + k * dc]
                    for k in range(WIN_LENGTH)
                ]

                score += evaluate_window(window)

    return score


def order_moves(board, moves, player):
    """Uu tien cac nuoc tao the co manh."""
    scored_moves = []

    for row, col in moves:
        board[row][col] = player
        score = evaluate_board(board)
        board[row][col] = EMPTY

        center = SIZE // 2
        distance = abs(row - center) + abs(col - center)

        scored_moves.append(
            (score - distance, (row, col))
        )

    reverse = player == ROBOT
    scored_moves.sort(key=lambda item: item[0], reverse=reverse)

    return [move for _, move in scored_moves]


def minimax(board, depth, alpha, beta, maximizing):
    winner = get_winner(board)

    if winner == ROBOT:
        return WIN_SCORE + depth, None

    if winner == HUMAN:
        return -WIN_SCORE - depth, None

    if is_full(board) or depth == 0:
        return evaluate_board(board), None

    moves = get_candidate_moves(board)

    if not moves:
        return evaluate_board(board), None

    current_player = ROBOT if maximizing else HUMAN
    moves = order_moves(board, moves, current_player)

    best_move = None

    if maximizing:
        best_score = -float("inf")

        for row, col in moves:
            board[row][col] = ROBOT
            score, _ = minimax(
                board, depth - 1, alpha, beta, False
            )
            board[row][col] = EMPTY

            if score > best_score:
                best_score = score
                best_move = (row, col)

            alpha = max(alpha, best_score)

            if beta <= alpha:
                break

        return best_score, best_move

    else:
        best_score = float("inf")

        for row, col in moves:
            board[row][col] = HUMAN
            score, _ = minimax(
                board, depth - 1, alpha, beta, True
            )
            board[row][col] = EMPTY

            if score < best_score:
                best_score = score
                best_move = (row, col)

            beta = min(beta, best_score)

            if beta <= alpha:
                break

        return best_score, best_move


def robot_move(board):
    """Chon nuoc di cho AI: Uu tien tim nuoc thang truoc, sau do moi den chan."""
    # 1. Tim nuoc thang ngay cho Robot truoc
    move = find_immediate_move(board, ROBOT)
    if move is not None:
        return move

    # 2. Neu Robot khong thang ngay duoc thi moi kiem tra va chan nguoi choi
    move = find_immediate_move(board, HUMAN)
    if move is not None:
        return move

    # 3. Dung Minimax cho cac truong hop con lai
    _, move = minimax(
        board,
        MAX_DEPTH,
        -float("inf"),
        float("inf"),
        True,
    )

    return move


def get_human_move(board):
    """Nhap nuoc di hoac dung game bang q/quit/exit."""
    while True:
        user_input = input(
            "Nhap hang cot (vi du: 2 3), "
            "hoac nhap q de dung game: "
        ).strip().lower()

        if user_input in ("q", "quit", "exit"):
            return None

        try:
            parts = user_input.split()

            if len(parts) != 2:
                raise ValueError

            row, col = map(int, parts)

        except ValueError:
            print("Nhap khong hop le. Hay nhap 2 so, vi du: 2 3.")
            continue

        if not is_valid_move(board, row, col):
            print("O khong hop le hoac da duoc danh. Hay thu lai.")
            continue

        return row, col


def save_final_board_image(board, filepath):
    """Ve va luu hinh anh ban co cuoi cung thanh file PNG."""
    fig, ax = plt.subplots(figsize=(6, 6))

    ax.set_xlim(-0.5, SIZE - 0.5)
    ax.set_ylim(-0.5, SIZE - 0.5)
    ax.set_xticks(range(SIZE))
    ax.set_yticks(range(SIZE))
    ax.grid(True, color="black", linewidth=1.5)
    ax.invert_yaxis()

    for r in range(SIZE):
        for c in range(SIZE):
            val = board[r][c]
            if val == HUMAN:
                ax.plot(c, r, 'o', color='blue', markersize=24, markeredgewidth=3, fillstyle='none')
            elif val == ROBOT:
                ax.plot(c, r, 'x', color='red', markersize=24, markeredgewidth=3)

    ax.set_title("Ban co ket thuc", fontsize=14, fontweight='bold')
    plt.tight_layout()
    plt.savefig(filepath, dpi=200)
    plt.close()
    print(f"[+] Da luu hinh anh ban co cuoi cung tai: {filepath}")


def save_winning_moves_data(board, txt_filepath, py_filepath):
    """Kiem tra va luu cac nuoc di kha thi de thang vao file TXT va PY."""
    robot_wins = find_all_winning_moves(board, ROBOT)
    human_wins = find_all_winning_moves(board, HUMAN)

    with open(txt_filepath, "w", encoding="utf-8") as f:
        f.write("=== CAC NUOC DI KHA THI DE THANG NGAY ===\n")
        f.write(f"Robot (O) winning moves: {robot_wins}\n")
        f.write(f"Human (X) winning moves: {human_wins}\n")

    with open(py_filepath, "w", encoding="utf-8") as f:
        f.write("# Data winning moves\n")
        f.write(f"winning_moves_data = {{\n")
        f.write(f"    'ROBOT_O': {robot_wins},\n")
        f.write(f"    'HUMAN_X': {human_wins}\n")
        f.write(f"}}\n")

    print(f"[+] Da luu nuoc di thang vao: {txt_filepath}")
    print(f"[+] Da luu du lieu nuoc di thang vao: {py_filepath}")


def play_game():
    board = create_board()

    os.makedirs(SAVE_DIR, exist_ok=True)

    print("=" * 40)
    print("       TIC-TAC-TOE 3x3 - 3 QUAN LIEN TIEP")
    print("=" * 40)
    print("Nguoi choi: X")
    print("Robot AI:  O")
    print("Nhap hang va cot tu 0 den 2.")
    print("AI dung Minimax + Alpha-Beta voi do sau toi da 9 nuoc.")
    print("Nhap q, quit hoac exit de dung game.")
    print_board(board)

    while True:
        # Luot nguoi choi
        print("Den luot ban (X).")

        move = get_human_move(board)

        if move is None:
            print("\nGame da duoc dung boi nguoi choi. Dang luu trang thai...")
            break

        row, col = move
        board[row][col] = HUMAN

        print_board(board)

        if get_winner(board) == HUMAN:
            print("Chuc mung! Ban (X) da thang!")
            break

        if is_full(board):
            print("Ban co hoa!")
            break

        # Luot robot
        print("Robot AI (O) dang suy nghi...")

        move = robot_move(board)

        if move is None:
            print("Khong con nuoc di. Game hoa!")
            break

        row, col = move
        board[row][col] = ROBOT

        print(f"Robot danh O tai hang {row}, cot {col}.")
        print_board(board)

        if get_winner(board) == ROBOT:
            print("Robot AI (O) da thang!")
            break

        if is_full(board):
            print("Ban hoa!")
            break

    print("\nBan co cuoi cung:")
    print_board(board)
    print("Game ket thuc.")


if __name__ == "__main__":
    try:
        play_game()
    except KeyboardInterrupt:
        print("\n\nDa nhan Ctrl+C. Game da dung.")