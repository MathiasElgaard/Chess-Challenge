using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    // Values of pieces: pawn, knight, bishop, rook, queen, king
    public readonly int[] pieceValues = { 100, 300, 300, 500, 900, 20000 };

    Timer timer;

    int maxThinkTime = 1000;

    int bestMoveEval;
    Move bestMove;

    int searchCount;

    public int StaticEvaluation(Board board)
    {
        int evaluation = 0;

        PieceList[] pieceLists = board.GetAllPieceLists();

        for (int i = 0; i < 6; i++)
        {
            evaluation += pieceValues[i] * pieceLists[i].Count;
            evaluation -= pieceValues[i] * pieceLists[i + 6].Count;
        }

        return board.IsWhiteToMove ? evaluation : -evaluation;
    }

    public int Search(Board board, int depth, int plyFromRoot, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > maxThinkTime)
            return 0;

        searchCount++;
        // Check for checkmate
        if (board.IsInCheckmate())
        {
            return -(20000 - plyFromRoot); // Checkmate
        }
        else if (board.IsDraw())
        {
            return 0; // Stalemate or draw
        }

        // Search depth reached, return static evaluation of the current position
        if (depth == 0)
        {
            return StaticEvaluation(board);
        }

        //Move[] moves = plyFromRoot == 0 ? bestMoves : board.GetLegalMoves();

        System.Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);
        if (plyFromRoot == 0 && bestMove != Move.NullMove)
        {
            int bestMoveIndex = moves.IndexOf(bestMove);
            moves[bestMoveIndex] = moves[0];
            moves[0] = bestMove;
        }

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(moves[i]);
            int eval = -Search(board, depth - 1, plyFromRoot + 1, -beta, -alpha);
            board.UndoMove(moves[i]);

            if (timer.MillisecondsElapsedThisTurn > maxThinkTime)
                return 0;

            if (eval >= beta)
            {
                return beta;
            }

            if (eval > alpha)
            {
                alpha = eval;
                if (plyFromRoot == 0)
                {
                    bestMoveEval = eval;
                    bestMove = moves[i];
                }
            }
        }

        return alpha;
    }
    public Move Think(Board board, Timer t)
    {
        timer = t;

        bestMove = Move.NullMove;

        for (int searchDepth = 1; searchDepth <= int.MaxValue; searchDepth++)
        {
            if (timer.MillisecondsElapsedThisTurn > maxThinkTime)
                break;

            searchCount = 0;
            bestMoveEval = Search(board, searchDepth, 0, -1000000, 1000000);
            Console.WriteLine("depth: " + searchDepth + ", best move: " + bestMove.ToString());
        }
        Console.WriteLine("Searched " + searchCount + " positions");

        return bestMove;
    }
}