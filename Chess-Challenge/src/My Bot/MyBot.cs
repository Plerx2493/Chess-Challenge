using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    private int movesEvaled = 0;
    private int movesPruned = 0;
    private int cacheUsed = 0;
    private bool botIsWhite;
    int[] moveScores = new int[220];
    const int maxMoveCount = 218;
    int depth = 7;
    public static ConcurrentDictionary<ulong, int> transpositionTable = new ConcurrentDictionary<ulong, int>();

    const int squareControlledByOpponentPawnPenalty = 350;
    const int capturedPieceValueMultiplier = 10;

    public Move Think(Board board, Timer timer)
    {
        Console.WriteLine("Thinking... (Cache size: " + transpositionTable.Count + ")");
        movesEvaled = 0;
        movesPruned = 0;
        cacheUsed = 0;
        botIsWhite = board.IsWhiteToMove;
        var eval = EvalMoves(board);

        Console.WriteLine($"{movesEvaled} Evaled and {movesPruned} pruned");
        Console.WriteLine($"Score: {eval.Item2}");
        Console.WriteLine($"Cache used: {cacheUsed}");
        return eval.Item1;
    }

    (Move, int) EvalMoves(Board board)
    {
        int bestScore = int.MinValue;
        Move bestMove = Move.NullMove;

        Move[] moves = board.GetLegalMoves();
        OrderMoves(board, moves);
        foreach (var move in moves)
        {
            int currentdepth = depth;
            if (board.PlyCount < 20)
                currentdepth = 4;
            board.MakeMove(move);
            int eval = MiniMax(currentdepth, board, int.MinValue, int.MaxValue, true);
            board.UndoMove(move);

            if (eval > bestScore)
            {
                bestScore = eval;
                bestMove = move;
            }
        }

        return (bestMove, bestScore);
    }

    int MiniMax(int depth, Board board, int alpha, int beta, bool isMax)
    {
        if (depth < 1)
        {
            if (transpositionTable.TryGetValue(board.ZobristKey, out var cache))
            {
                cacheUsed++;
                return cache;
            }

            movesEvaled++;
            var val = EvalBoard(board, board.IsWhiteToMove);
            transpositionTable[board.ZobristKey] = val;
            return val;
        }

        var moves = board.GetLegalMoves();
        if (moves.Length < 1)
        {
            if (board.IsInCheck())
            {
                int mateScore = 10000;
                return -mateScore;
            }
            else
            {
                return 0;
            }
        }

        OrderMoves(board, moves);
        int bestValue;
        if (isMax)
        {
            bestValue = int.MinValue;
            foreach (var newMove in moves)
            {
                board.MakeMove(newMove);
                int eval = MiniMax(depth - 1, board, beta, alpha, !isMax);
                board.UndoMove(newMove);
                bestValue = Math.Max(eval, bestValue);
                alpha = Math.Max(alpha, bestValue);
                if (beta <= alpha)
                {
                    movesPruned++;
                    break;
                }
            }

            return bestValue;
        }

        bestValue = int.MaxValue;
        foreach (var newMove in moves)
        {
            board.MakeMove(newMove);
            int eval = MiniMax(depth - 1, board, beta, alpha, !isMax);
            board.UndoMove(newMove);
            bestValue = Math.Min(eval, bestValue);
            beta = Math.Min(alpha, bestValue);
            if (beta <= alpha)
            {
                movesPruned++;
                break;
            }
        }

        return bestValue;
    }

    int EvalBoard(Board board, bool isWhite)
    {
        int score = 0;
        score += EvalBoardOneSide(board, isWhite);
        score -= EvalBoardOneSide(board, !isWhite);
        return score;
    }

    int EvalBoardOneSide(Board board, bool isWhite)
    {
        int score = 0;
        foreach (var piece in board.GetPieceList(PieceType.Pawn, isWhite))
        {
            score += 100;
        }

        foreach (var piece in board.GetPieceList(PieceType.Knight, isWhite))
        {
            score += 300;
        }

        foreach (var piece in board.GetPieceList(PieceType.Bishop, isWhite))
        {
            score += 300;
        }

        foreach (var piece in board.GetPieceList(PieceType.Rook, isWhite))
        {
            score += 500;
        }

        foreach (var piece in board.GetPieceList(PieceType.Queen, isWhite))
        {
            score += 900;
        }

        foreach (var piece in board.GetPieceList(PieceType.King, isWhite))
        {
            score += 10000;
        }

        return score;
    }

    public void OrderMoves(Board board, Move[] moves)
    {
        for (int i = 0; i < moves.Length; i++)
        {
            int score = 0;
            PieceType movePieceType = board.GetPiece(moves[i].StartSquare).PieceType;
            PieceType capturePieceType = board.GetPiece(moves[i].TargetSquare).PieceType;
            PieceType flag = moves[i].PromotionPieceType;

            if (capturePieceType != PieceType.None)
            {
                // Order moves to try capturing the most valuable opponent piece with least valuable of own pieces first
                // The capturedPieceValueMultiplier is used to make even 'bad' captures like QxP rank above non-captures
                score = capturedPieceValueMultiplier * GetPieceValue(capturePieceType) - GetPieceValue(movePieceType);
            }

            if (movePieceType == PieceType.Pawn)
            {
                if (flag == PieceType.Queen)
                {
                    score += 900;
                }
                else if (flag == PieceType.Knight)
                {
                    score += 300;
                }
                else if (flag == PieceType.Rook)
                {
                    score += 500;
                }
                else if (flag == PieceType.Bishop)
                {
                    score += 320;
                }
            }

            board.MakeMove(moves[i]);
            // reduce score if square is attacked by opponent
            foreach (var newMoves in board.GetLegalMoves(true))
            {
                if (newMoves.TargetSquare == moves[i].TargetSquare)
                {
                    score -= squareControlledByOpponentPawnPenalty;
                }
            }

            board.UndoMove(moves[i]);

            moveScores[i] = score;
        }

        Sort(moves);
    }

    static int GetPieceValue(PieceType pieceType)
    {
        switch (pieceType)
        {
            case PieceType.Queen:
                return 900;
            case PieceType.Rook:
                return 500;
            case PieceType.Knight:
                return 300;
            case PieceType.Bishop:
                return 320;
            case PieceType.Pawn:
                return 100;
            default:
                return 0;
        }
    }

    void Sort(Move[] moves)
    {
        // Sort the moves list based on scores
        for (int i = 0; i < moves.Length - 1; i++)
        {
            for (int j = i + 1; j > 0; j--)
            {
                int swapIndex = j - 1;
                if (moveScores[swapIndex] < moveScores[j])
                {
                    (moves[j], moves[swapIndex]) = (moves[swapIndex], moves[j]);
                    (moveScores[j], moveScores[swapIndex]) = (moveScores[swapIndex], moveScores[j]);
                }
            }
        }
    }
}