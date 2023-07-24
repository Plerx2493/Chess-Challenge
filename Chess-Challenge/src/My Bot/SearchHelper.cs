using System;
using ChessChallenge.API;

public partial class MyBot
{
    int Quiesce(Board board, int alpha, int beta ) {
        int stand_pat = EvalBoard(board, _botIsWhite);
        if( stand_pat >= beta )
            return beta;
        if( alpha < stand_pat )
            alpha = stand_pat;

        Span<Move> captureMoves = stackalloc Move[MaxMoveCount];
        board.GetLegalMovesNonAlloc(ref captureMoves, true);
        
        foreach (Move move in captureMoves){
            board.MakeMove(move);
            int score = -Quiesce(board,  -beta, -alpha );
            board.UndoMove(move);

            if( score >= beta )
                return beta;
            if( score > alpha )
                alpha = score;
        }
        return alpha;
    }
}