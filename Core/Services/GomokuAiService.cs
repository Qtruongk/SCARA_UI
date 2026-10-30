using System;
using System.Collections.Generic;

namespace Test_1.Services
{
    /// <summary>
    /// Thuật toán Trí tuệ nhân tạo (AI) chơi cờ Caro / Tic-Tac-Toe 3x3.
    /// Dựa trên thuật toán Minimax kết hợp cắt tỉa Alpha-Beta và Heuristic ưu tiên nước thắng / chặn.
    /// Port chính xác từ mô hình gomoku_ai_3x3.py.
    /// </summary>
    public class GomokuAiService
    {
        public const int Size = 3;
        public const int WinLength = 3;
        public const char Empty = '.';
        public const char Human = 'X';
        public const char Robot = 'O';

        private const int MaxDepth = Size * Size; // 9
        private const int WinScore = 1_000_000;

        private static readonly (int dr, int dc)[] Directions = new[]
        {
            (0, 1),   // Ngang
            (1, 0),   // Dọc
            (1, 1),   // Chéo xuống
            (1, -1)   // Chéo lên
        };

        public static char[,] CreateBoard()
        {
            char[,] board = new char[Size, Size];
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    board[r, c] = Empty;
            return board;
        }

        public static bool IsValidMove(char[,] board, int row, int col)
        {
            return row >= 0 && row < Size
                && col >= 0 && col < Size
                && board[row, col] == Empty;
        }

        public static bool IsFull(char[,] board)
        {
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (board[r, c] == Empty) return false;
            return true;
        }

        public static char? GetWinner(char[,] board)
        {
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    char player = board[r, c];
                    if (player == Empty) continue;

                    foreach (var (dr, dc) in Directions)
                    {
                        int endR = r + (WinLength - 1) * dr;
                        int endC = c + (WinLength - 1) * dc;
                        if (endR < 0 || endR >= Size || endC < 0 || endC >= Size) continue;

                        bool won = true;
                        for (int k = 1; k < WinLength; k++)
                        {
                            if (board[r + k * dr, c + k * dc] != player)
                            {
                                won = false;
                                break;
                            }
                        }
                        if (won) return player;
                    }
                }
            }
            return null;
        }

        public static bool HasWinningMove(char[,] board, char player, int row, int col)
        {
            foreach (var (dr, dc) in Directions)
            {
                int count = 1;
                foreach (int sign in new[] { 1, -1 })
                {
                    int nr = row + sign * dr;
                    int nc = col + sign * dc;

                    while (nr >= 0 && nr < Size && nc >= 0 && nc < Size && board[nr, nc] == player)
                    {
                        count++;
                        nr += sign * dr;
                        nc += sign * dc;
                    }
                }

                if (count >= WinLength) return true;
            }
            return false;
        }

        public static List<(int r, int c)> GetCandidateMoves(char[,] board)
        {
            var occupied = new List<(int r, int c)>();
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (board[r, c] != Empty)
                        occupied.Add((r, c));

            if (occupied.Count == 0)
                return new List<(int r, int c)> { (Size / 2, Size / 2) };

            var candidates = new HashSet<(int r, int c)>();
            foreach (var (r, c) in occupied)
            {
                for (int dr = -2; dr <= 2; dr++)
                {
                    for (int dc = -2; dc <= 2; dc++)
                    {
                        int nr = r + dr;
                        int nc = c + dc;
                        if (nr >= 0 && nr < Size && nc >= 0 && nc < Size && board[nr, nc] == Empty)
                        {
                            candidates.Add((nr, nc));
                        }
                    }
                }
            }

            return new List<(int r, int c)>(candidates);
        }

        public static (int r, int c)? FindImmediateMove(char[,] board, char player)
        {
            foreach (var (r, c) in GetCandidateMoves(board))
            {
                board[r, c] = player;
                bool wins = HasWinningMove(board, player, r, c);
                board[r, c] = Empty;

                if (wins) return (r, c);
            }
            return null;
        }

        public static int EvaluateWindow(char[] window)
        {
            int robotCount = 0;
            int humanCount = 0;
            for (int i = 0; i < window.Length; i++)
            {
                if (window[i] == Robot) robotCount++;
                else if (window[i] == Human) humanCount++;
            }

            if (robotCount > 0 && humanCount > 0) return 0;

            if (robotCount > 0)
            {
                if (robotCount == 1) return 10;
                if (robotCount == 2) return 100;
                if (robotCount >= 3) return WinScore;
            }

            if (humanCount > 0)
            {
                if (humanCount == 1) return -10;
                if (humanCount == 2) return -100;
                if (humanCount >= 3) return -WinScore;
            }

            return 0;
        }

        public static int EvaluateBoard(char[,] board)
        {
            int score = 0;
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    foreach (var (dr, dc) in Directions)
                    {
                        int endR = r + (WinLength - 1) * dr;
                        int endC = c + (WinLength - 1) * dc;
                        if (endR < 0 || endR >= Size || endC < 0 || endC >= Size) continue;

                        char[] window = new char[WinLength];
                        for (int k = 0; k < WinLength; k++)
                        {
                            window[k] = board[r + k * dr, c + k * dc];
                        }
                        score += EvaluateWindow(window);
                    }
                }
            }
            return score;
        }

        private static List<(int r, int c)> OrderMoves(char[,] board, List<(int r, int c)> moves, char player)
        {
            var scored = new List<(int score, (int r, int c) move)>(moves.Count);
            int center = Size / 2;

            foreach (var (r, c) in moves)
            {
                board[r, c] = player;
                int score = EvaluateBoard(board);
                board[r, c] = Empty;

                int dist = Math.Abs(r - center) + Math.Abs(c - center);
                scored.Add((score - dist, (r, c)));
            }

            if (player == Robot)
                scored.Sort((a, b) => b.score.CompareTo(a.score));
            else
                scored.Sort((a, b) => a.score.CompareTo(b.score));

            var result = new List<(int r, int c)>(scored.Count);
            foreach (var item in scored)
                result.Add(item.move);
            return result;
        }

        public static (int score, (int r, int c)? bestMove) Minimax(char[,] board, int depth, int alpha, int beta, bool maximizing)
        {
            char? winner = GetWinner(board);
            if (winner == Robot) return (WinScore + depth, null);
            if (winner == Human) return (-WinScore - depth, null);
            if (IsFull(board) || depth == 0) return (EvaluateBoard(board), null);

            var moves = GetCandidateMoves(board);
            if (moves.Count == 0) return (EvaluateBoard(board), null);

            char currentPlayer = maximizing ? Robot : Human;
            moves = OrderMoves(board, moves, currentPlayer);

            (int r, int c)? bestMove = null;

            if (maximizing)
            {
                int bestScore = -int.MaxValue / 2;
                foreach (var (r, c) in moves)
                {
                    board[r, c] = Robot;
                    var (score, _) = Minimax(board, depth - 1, alpha, beta, false);
                    board[r, c] = Empty;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMove = (r, c);
                    }

                    alpha = Math.Max(alpha, bestScore);
                    if (beta <= alpha) break;
                }
                return (bestScore, bestMove);
            }
            else
            {
                int bestScore = int.MaxValue / 2;
                foreach (var (r, c) in moves)
                {
                    board[r, c] = Human;
                    var (score, _) = Minimax(board, depth - 1, alpha, beta, true);
                    board[r, c] = Empty;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestMove = (r, c);
                    }

                    beta = Math.Min(beta, bestScore);
                    if (beta <= alpha) break;
                }
                return (bestScore, bestMove);
            }
        }

        /// <summary>
        /// Tính toán nước đi thông minh nhất cho Robot AI ('O') trên bàn cờ.
        /// Trả về (row, col) hoặc null nếu không còn nước đi hợp lệ.
        /// </summary>
        public static (int r, int c)? FindBestMove(char[,] board)
        {
            // 1. Tìm nước thắng ngay cho Robot trước
            var winMove = FindImmediateMove(board, Robot);
            if (winMove.HasValue) return winMove;

            // 2. Nếu Robot không thắng ngay được thì kiểm tra và chặn người chơi Human
            var blockMove = FindImmediateMove(board, Human);
            if (blockMove.HasValue) return blockMove;

            // 3. Dùng Minimax Alpha-Beta cho các trường hợp còn lại
            var (_, bestMove) = Minimax(board, MaxDepth, -int.MaxValue / 2, int.MaxValue / 2, true);
            return bestMove;
        }
    }
}
