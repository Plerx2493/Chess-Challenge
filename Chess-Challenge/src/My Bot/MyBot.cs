using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ChessChallenge.API;

using static System.Math;

public partial class MyBot : IChessBot
{
    int _movesEvaled, _movesPruned, _cacheUsed = 0;
    int _timeMs = 200 * 1;
    bool _botIsWhite;
    int[] _moveScores = new int[MaxMoveCount];
    const int MaxMoveCount = 218;
    
    ConcurrentDictionary<ulong, int> _transpositionTable = new();
    readonly int _depth = 6;
    readonly int _squareControlledByOpponentPawnPenalty = 350;
    readonly int _capturedPieceValueMultiplier = 10;
    readonly int[] _pieceValues = new [] { 0, 100, 300, 320, 500, 900, 10000};
    
    
    
    Move lastMove = Move.NullMove;
    
    (Move, int) _bestLastEval = (Move.NullMove, int.MinValue);
 
    public Move Think(Board board, Timer timer)
    {
        Console.WriteLine("Thinking... (Cache size: " + _transpositionTable.Count + ")..................");
        _movesEvaled = 0;
        _movesPruned = 0;
        _cacheUsed = 0;
        _botIsWhite = board.IsWhiteToMove;
        int depthReached = 0;

        _timeMs = GetTime(board, timer);
        
        bool isAborted = false;
        (Move, int) bestEval = (Move.NullMove, int.MinValue);
        for (int i = 0; i < 20; i++)
        {
            if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
            var evalIt = EvalMoves(board, i + 1, timer);
            if (evalIt.Item2 > bestEval.Item2 && evalIt.Item2 != int.MaxValue && evalIt.Item2 != int.MinValue)
                bestEval = evalIt;
            _bestLastEval = bestEval;
            depthReached++;
        }

        Console.WriteLine($"{_movesEvaled} Evaled and {_movesPruned} pruned");
        Console.WriteLine($"Score: {bestEval.Item2}");
        Console.WriteLine($"Cache used: {_cacheUsed}");
        Console.WriteLine($"depth reached: {depthReached}");
        
        lastMove = bestEval.Item1;
        return bestEval.Item1;
    }

    (Move, int) EvalMoves(Board board, int depth, Timer timer)
    {
        int bestScore = int.MinValue;
        Move bestMove = Move.NullMove;
        
        System.Span<Move> moves = stackalloc Move[MaxMoveCount];
        board.GetLegalMovesNonAlloc(ref moves);
        OrderMoves(board, ref moves);
        foreach (var move in moves)
        {
            if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
            board.MakeMove(move);
            int eval = MiniMax(depth, board, int.MinValue, int.MaxValue, true, timer);
            board.UndoMove(move);

            if (eval > bestScore)
            {
                bestScore = eval;
                bestMove = move;
            }
        }

        return (bestMove, bestScore);
    }

    int MiniMax(int depth, Board board, int alpha, int beta, bool isMax, Timer timer)
    {
        if (depth < 1)
        {
            if (_transpositionTable.TryGetValue(board.ZobristKey, out var cache))
            {
                _cacheUsed++;
                return cache;
            }

            _movesEvaled++;
            var val = Quiesce(board, -999999, 999999);
            //var val = EvalBoard(board, _botIsWhite);
            _transpositionTable[board.ZobristKey] = val;
            return val;
        }

        Span<Move> moves = stackalloc Move[MaxMoveCount];
        board.GetLegalMovesNonAlloc(ref moves);
        if (moves.Length < 1)
        {
            if (board.IsInCheck())
            {
                return -int.MaxValue;
            }
            else
            {
                return 0;
            }
        }

        OrderMoves(board, ref moves);
        int bestValue = 0;
        if (isMax)
        {
            bestValue = int.MinValue;
            foreach (var newMove in moves)
            {
                if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
                board.MakeMove(newMove);
                bestValue = MiniMax(depth - 1, board, alpha, beta, !isMax, timer);
                board.UndoMove(newMove);
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
            if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
            board.MakeMove(newMove);
            bestValue = MiniMax(depth - 1, board, alpha, beta, !isMax, timer);
            board.UndoMove(newMove);
            beta = Min(beta, bestValue);
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
        if (board.IsInCheckmate() || board.IsInsufficientMaterial())
        {
            if(isWhite == _botIsWhite) 
                return -999999;
            else
                return 999999;
        }
        
        if(board.IsRepeatedPosition()) score -= 100;
        score += EvalBoardOneSide(board, isWhite);
        score -= EvalBoardOneSide(board, !isWhite);
        return score;
    }

    int EvalBoardOneSide(Board board, bool isWhite)
    {
        int score = 0;
        
        foreach (var pieceList in board.GetAllPieceLists())
        {
            if (pieceList.IsWhitePieceList != isWhite) continue;
            foreach (var piece in pieceList)
            {
                score += _pieceValues[(int)piece.PieceType];
                if (board.SquareIsAttackedByOpponent(piece.Square))
                    score -= _squareControlledByOpponentPawnPenalty;
                if (piece.IsPawn)
                    score += pawnScores[piece.Square.Rank, piece.Square.File];
                
                if (piece.IsBishop)
                    score += bishopScores[piece.Square.Rank, piece.Square.File];
                
                if (piece.IsKnight)
                    score += knightScores[piece.Square.Rank, piece.Square.File];
                
                if (piece.IsRook)
                    score += rookScores[piece.Square.Rank, piece.Square.File];
                
                if (piece.IsQueen)
                    score += queenScores[piece.Square.Rank, piece.Square.File];
                
                if (piece.IsKing)
                    score += kingScores[piece.Square.Rank, piece.Square.File];
                
                if (piece.IsKing && board.IsInCheck())
                    score -= 100;
                    
            }
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

            if (move.Equals(_bestLastEval.Item1))
            {
                score = int.MaxValue;
                i++;
                continue;
            }
            
            if (movePieceType == PieceType.King) 
                score -= 1000;
            
            //discourage moving the same piece twice in a row
            if (movePieceType == lastMove.MovePieceType && move.StartSquare == lastMove.TargetSquare)
                score -= 1000;
            
            if (board.GameMoveHistory.Contains(move))
                score -= 1000;
            
            if (capturePieceType != PieceType.None)
                score = _capturedPieceValueMultiplier * _pieceValues[(int)capturePieceType] - _pieceValues[(int)movePieceType];

            if (movePieceType == PieceType.Pawn) 
                score += _pieceValues[(int)flag];
            
            if (move.IsCastles) score += 500;
            
            board.MakeMove(move);
            if (board.IsInCheckmate()) score += 10000;
            if (board.IsInCheck()) score += 500;
            if (board.IsRepeatedPosition()) score -= 1000;
            if (board.IsInsufficientMaterial()) score += 1000;
            
            // reduce score if square is attacked by opponent
            Span<Move> opponentMoves = stackalloc Move[MaxMoveCount];
            board.GetLegalMovesNonAlloc(ref opponentMoves);
            if (opponentMoves.Length < 1) score += 1000;
            
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
    
    // Basic time management
    public int GetTime(Board board, Timer timer)
    {
        return Min(board.PlyCount * 150 + 100, timer.MillisecondsRemaining / 20);
    }
}