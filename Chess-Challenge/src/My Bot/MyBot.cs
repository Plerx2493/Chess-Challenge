using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ChessChallenge.API;

using static System.Math;

public class MyBot : IChessBot
{
    int _movesEvaled, _movesPruned, _cacheUsed = 0;
    bool _botIsWhite;
    int[] _moveScores = new int[MaxMoveCount];
    const int MaxMoveCount = 218;
    
    static ConcurrentDictionary<ulong, int> _transpositionTable = new();
    readonly int _depth = 6;
    readonly int _squareControlledByOpponentPawnPenalty = 350;
    readonly int _capturedPieceValueMultiplier = 10;
    readonly int[] _pieceValues = new [] { 0, 100, 300, 320, 500, 900, 10000};
 
    public Move Think(Board board, Timer timer)
    {
        Console.WriteLine("Thinking... (Cache size: " + _transpositionTable.Count + ")");
        _movesEvaled = 0;
        _movesPruned = 0;
        _cacheUsed = 0;
        _botIsWhite = board.IsWhiteToMove;
        var eval = EvalMoves(board);

        Console.WriteLine($"{_movesEvaled} Evaled and {_movesPruned} pruned");
        Console.WriteLine($"Score: {eval.Item2}");
        Console.WriteLine($"Cache used: {_cacheUsed}");
        return eval.Item1;
    }

    (Move, int) EvalMoves(Board board)
    {
        int bestScore = int.MinValue;
        Move bestMove = Move.NullMove;
        
        System.Span<Move> moves = stackalloc Move[MaxMoveCount];
        board.GetLegalMovesNonAlloc(ref moves);
        OrderMoves(board, ref moves);
        foreach (var move in moves)
        {
            int currentdepth = _depth;
            if (board.PlyCount < 20) currentdepth = 4;
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
            if (_transpositionTable.TryGetValue(board.ZobristKey, out var cache))
            {
                _cacheUsed++;
                return cache;
            }

            _movesEvaled++;
            var val = EvalBoard(board, board.IsWhiteToMove);
            _transpositionTable[board.ZobristKey] = val;
            return val;
        }

        Span<Move> moves = stackalloc Move[MaxMoveCount];
        board.GetLegalMovesNonAlloc(ref moves);
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

        OrderMoves(board, ref moves);
        int bestValue;
        if (isMax)
        {
            bestValue = int.MinValue;
            foreach (var newMove in moves)
            {
                board.MakeMove(newMove);
                int eval = MiniMax(depth - 1, board, beta, alpha, !isMax);
                board.UndoMove(newMove);
                bestValue = Max(eval, bestValue);
                alpha = Max(alpha, bestValue);
                if (beta <= alpha)
                {
                    _movesPruned++;
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
            bestValue = Min(eval, bestValue);
            beta = Min(alpha, bestValue);
            if (beta <= alpha)
            {
                _movesPruned++;
                break;
            }
        }

        return bestValue;
    }

    int EvalBoard(Board board, bool isWhite)
    {
        int score = 0;
        if(board.IsInCheckmate()) return Int32.MinValue;
        if(board.IsDraw()) return 0;
        if(board.IsInCheck()) score -= 500;
        score += EvalBoardOneSide(board, isWhite);
        score -= EvalBoardOneSide(board, !isWhite);
        return score;
    }

    int EvalBoardOneSide(Board board, bool isWhite)
    {
        int score = 0;
        foreach (var pieceList in board.GetAllPieceLists())
        {
            if(pieceList.IsWhitePieceList != isWhite) continue;
            foreach (var pice in pieceList)
                score += _pieceValues[(int)pice.PieceType];
        }
        
        return score;
    }

    public void OrderMoves(Board board, ref Span<Move> moves)
    {
        int i = 0;
        foreach (var move in moves)
        {
            int score = 0;
            PieceType movePieceType = move.MovePieceType;
            PieceType capturePieceType = move.CapturePieceType;
            PieceType flag = move.PromotionPieceType;
            
            if (capturePieceType != PieceType.None)
                score = _capturedPieceValueMultiplier * _pieceValues[(int)capturePieceType] - _pieceValues[(int)movePieceType];

            if (movePieceType == PieceType.Pawn) score += _pieceValues[(int)flag] - 100;
            
            board.MakeMove(move);
            if(board.IsInCheckmate()) score += 10000;
            if(board.IsInCheck()) score += 500;
            if (board.IsDraw()) score -= 1000;
            
            // reduce score if square is attacked by opponent
            Span<Move> opponentMoves = stackalloc Move[MaxMoveCount];
            board.GetLegalMovesNonAlloc(ref opponentMoves);
            foreach (var newMoves in opponentMoves)
            {
                if (newMoves.TargetSquare == move.TargetSquare)
                    score -= _squareControlledByOpponentPawnPenalty;
            }

            board.UndoMove(move);

            _moveScores[i] = score;
            i++;
        }

        Sort(ref moves);
    }
    
    void Sort(ref Span<Move> moves)
    {
        // Sort the moves list based on scores
        for (int i = 0; i < moves.Length - 1; i++)
            for (int j = i + 1; j > 0; j--)
            {
                int swapIndex = j - 1;
                if (_moveScores[swapIndex] < _moveScores[j])
                {
                    (moves[j], moves[swapIndex]) = (moves[swapIndex], moves[j]);
                    (_moveScores[j], _moveScores[swapIndex]) = (_moveScores[swapIndex], _moveScores[j]);
                }
            }
    }
}