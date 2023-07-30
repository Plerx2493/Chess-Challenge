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
    bool useTT = true;
    bool clearTT = true;
    bool useQSearch = true;
    readonly int _depth = 6;
    readonly int _squareControlledByOpponentPawnPenalty = 500;
    readonly int _capturedPieceValueMultiplier = 10;
    readonly int[] _pieceValues = new [] { 0, 100, 300, 320, 500, 900, 10000};
    
    
    
    Move lastMove = Move.NullMove;
    
    (Move, int) _bestLastEval = (Move.NullMove, int.MinValue);
 
    public Move Think(Board board, Timer timer)
    {
        //Console.WriteLine("Thinking... (Cache size: " + _transpositionTable.Count + ")..................");
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
            if (clearTT) _transpositionTable.Clear();
            var evalIt = EvalMoves(board, i + 1, timer);
            if (evalIt.Item2 > bestEval.Item2 && evalIt.Item2 != int.MaxValue && evalIt.Item2 != int.MinValue)
                bestEval = evalIt;
            _bestLastEval = bestEval;
            depthReached++;
            if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
        }

        //Console.WriteLine($"{_movesEvaled} Evaled and {_movesPruned} pruned");
        Console.WriteLine($"Score: {bestEval.Item2}");
        //Console.WriteLine($"Cache used: {_cacheUsed}");
        //Console.WriteLine($"depth reached: {depthReached}");
        
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
            board.MakeMove(move);
            int eval = MiniMax(depth, board, int.MinValue, int.MaxValue, true, timer);
            board.UndoMove(move);

            if (eval > bestScore)
            {
                bestScore = eval;
                bestMove = move;
            }
            if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
        }

        return (bestMove, bestScore);
    }

    int MiniMax(int depth, Board board, int alpha, int beta, bool isMax, Timer timer)
    {
        if (depth < 1)
        {
            if (useTT && _transpositionTable.TryGetValue(board.ZobristKey, out var cache))
            {
                _cacheUsed++;
                return cache;
            }

            _movesEvaled++;
            int val = 0;
            if (useQSearch)
                val = Quiesce(board, -999999, 999999);
            else
                val = EvalBoard(board, _botIsWhite);
            if (useTT) _transpositionTable[board.ZobristKey] = val;
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
                board.MakeMove(newMove);
                bestValue = MiniMax(depth - 1, board, alpha, beta, !isMax, timer);
                board.UndoMove(newMove);
                alpha = Max(alpha, bestValue);
                if (beta <= alpha)
                {
                    _movesPruned++;
                    break;
                }
                if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
            }

            return bestValue;
        }
        
        bestValue = int.MaxValue;
        foreach (var newMove in moves)
        {
            board.MakeMove(newMove);
            bestValue = MiniMax(depth - 1, board, alpha, beta, !isMax, timer);
            board.UndoMove(newMove);
            beta = Min(beta, bestValue);
            if (beta <= alpha)
            {
                _movesPruned++;
                break;
            }
            if (timer.MillisecondsElapsedThisTurn > _timeMs) break;
        }

        return bestValue;
    }

    int EvalBoard(Board board, bool isWhite)
    {
        int score = 0;
        if (board.IsInCheckmate() || board.IsInsufficientMaterial() )
        {
            if(isWhite == _botIsWhite) 
                return 999999;
            else
                return -999999;
        }

        if (board.FiftyMoveCounter >= 100) return 0;
        
        if(board.IsRepeatedPosition()) score -= 1000;
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
                
                /*
                if (board.SquareIsAttackedByOpponent(piece.Square))
                    if (isWhite == _botIsWhite)
                        score -= _squareControlledByOpponentPawnPenalty;
                    else
                        score += _squareControlledByOpponentPawnPenalty;
                */
                
                if (piece.IsPawn)
                    if (_botIsWhite == isWhite)
                        score += pawnScores[piece.Square.Rank, piece.Square.File];
                    else
                        score += pawnScores[7 - piece.Square.Rank, piece.Square.File];
                
                if (piece.IsBishop)
                    if (_botIsWhite == isWhite)
                        score += bishopScores[piece.Square.Rank, piece.Square.File];
                    else
                        score += bishopScores[7 - piece.Square.Rank, piece.Square.File];
                
                if (piece.IsKnight)
                    if (_botIsWhite == isWhite)
                        score += knightScores[piece.Square.Rank, piece.Square.File];
                    else
                        score += knightScores[7 - piece.Square.Rank, piece.Square.File];
                
                if (piece.IsRook)
                    if (_botIsWhite == isWhite)
                        score += rookScores[piece.Square.Rank, piece.Square.File];
                    else
                        score += rookScores[7 - piece.Square.Rank, piece.Square.File];
                
                if (piece.IsQueen)
                    if (_botIsWhite == isWhite)
                        score += queenScores[piece.Square.Rank, piece.Square.File];
                    else
                        score += queenScores[7 - piece.Square.Rank, piece.Square.File];
                
                if (piece.IsKing)
                    if (_botIsWhite == isWhite)
                        score += kingScores[piece.Square.Rank, piece.Square.File];
                    else
                        score += kingScores[7 - piece.Square.Rank, piece.Square.File];
                    
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
    
    int Quiesce(Board board, int alpha, int beta)
    {
        Span<Move> captureMoves = stackalloc Move[MaxMoveCount];
        board.GetLegalMovesNonAlloc(ref captureMoves, true);

        if (captureMoves.Length < 1)
        {
            int stand_pat;
            if (_transpositionTable.TryGetValue(board.ZobristKey, out var cache))
            {
                _cacheUsed++;
                stand_pat = cache;
            }
            else
            {
                _movesEvaled++;
                stand_pat = EvalBoard(board, _botIsWhite);
                _transpositionTable[board.ZobristKey] = stand_pat;
            }
            
            if (stand_pat >= beta)
            {
                _movesPruned++;
                return beta;
            }

            if (alpha < stand_pat)
                alpha = stand_pat;
        }

        OrderMoves(board, ref captureMoves);
        foreach (Move move in captureMoves)
        {
            board.MakeMove(move);
            int score = -Quiesce(board, -beta, -alpha);
            board.UndoMove(move);

            if (score >= beta)
            {
                _movesPruned++;
                return beta;
            }

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    // Scores for pawns, knights, bishops, rooks, queens, kings
    sbyte[,] pawnScores =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0 },
        { 50, 50, 50, 50, 50, 50, 50, 50 },
        { 10, 10, 20, 30, 30, 20, 10, 10 },
        { 5, 5, 10, 25, 25, 10, 5, 5 },
        { 0, 0, 0, 20, 20, 0, 0, 0 },
        { 5, -5, -10, 0, 0, -10, -5, 5 },
        { 5, 10, 10, -20, -20, 10, 10, 5 },
        { 0, 0, 0, 0, 0, 0, 0, 0 }
    };
    
    sbyte[,] knightScores =
    {
        { -50, -40, -30, -30, -30, -30, -40, -50 },
        { -40, -20, 0, 0, 0, 0, -20, -40 },
        { -30, 0, 10, 15, 15, 10, 0, -30 },
        { -30, 5, 15, 20, 20, 15, 5, -30 },
        { -30, 0, 15, 20, 20, 15, 0, -30 },
        { -30, 5, 10, 15, 15, 10, 5, -30 },
        { -40, -20, 0, 5, 5, 0, -20, -40 },
        { -50, -40, -30, -30, -30, -30, -40, -50 }
    };
    
    sbyte[,] bishopScores =
    {
        { -20, -10, -10, -10, -10, -10, -10, -20 },
        { -10, 0, 0, 0, 0, 0, 0, -10 },
        { -10, 0, 5, 10, 10, 5, 0, -10 },
        { -10, 5, 5, 10, 10, 5, 5, -10 },
        { -10, 0, 10, 10, 10, 10, 0, -10 },
        { -10, 10, 10, 10, 10, 10, 10, -10 },
        { -10, 5, 0, 0, 0, 0, 5, -10 },
        { -20, -10, -10, -10, -10, -10, -10, -20 }
    };
    
    sbyte[,] rookScores =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0 },
        { 5, 10, 10, 10, 10, 10, 10, 5 },
        { -5, 0, 0, 0, 0, 0, 0, -5 },
        { -5, 0, 0, 0, 0, 0, 0, -5 },
        { -5, 0, 0, 0, 0, 0, 0, -5 },
        { -5, 0, 0, 0, 0, 0, 0, -5 },
        { -5, 0, 0, 0, 0, 0, 0, -5 },
        { 0, 0, 0, 5, 5, 0, 0, 0 }
    };
    
    sbyte[,] queenScores =
    {
        { -20, -10, -10, -5, -5, -10, -10, -20 },
        { -10, 0, 0, 0, 0, 0, 0, -10 },
        { -10, 0, 5, 5, 5, 5, 0, -10 },
        { -5, 0, 5, 5, 5, 5, 0, -5 },
        { 0, 0, 5, 5, 5, 5, 0, -5 },
        { -10, 5, 5, 5, 5, 5, 0, -10 },
        { -10, 0, 5, 0, 0, 0, 0, -10 },
        { -20, -10, -10, -5, -5, -10, -10, -20 }
    };
    
    sbyte[,] kingScores =
    {
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -30, -40, -40, -50, -50, -40, -40, -30 },
        { -20, -30, -30, -40, -40, -30, -30, -20 },
        { -10, -20, -20, -20, -20, -20, -20, -10 },
        { 20, 20, 0, 0, 0, 0, 20, 20 },
        { 20, 30, 10, 0, 0, 10, 30, 20 }
    };
}