using ChessChallenge.API;
using System;
using System.Numerics;

public class MyBot : IChessBot
{
    // Values of pieces: none, pawn, knight, bishop, rook, queen, king
    public readonly int[] pieceValues = { 0, 100, 300, 300, 500, 900, 20000 };

    // Piece-square table (biased by +50, to avoid tokens spent on negative signs)
    public readonly int[] pieceSquareTable = {
        // Pawns
         50,  50,  50,  50,  50,  50,  50,  50,
        100, 100, 100, 100, 100, 100, 100, 100,
         60,  60,  70,  80,  80,  70,  60,  60,
         55,  55,  60,  75,  75,  60,  55,  55,
         50,  50,  50,  70,  70,  50,  50,  50,
         55,  55,  40,  50,  50,  40,  45,  55,
         55,  60,  60,  30,  30,  60,  60,  55,
         50,  50,  50,  50,  50,  50,  50,  50,
        // Knights
          0,  10,  20,  20,  20,  20,  10,   0,
         10,  30,  50,  50,  50,  50,  30,  10,
         20,  50,  60,  65,  65,  60,  50,  20,
         20,  55,  65,  70,  70,  65,  55,  20,
         20,  50,  65,  70,  70,  65,  50,  20,
         20,  55,  60,  65,  65,  60,  55,  20,
         10,  30,  50,  55,  55,  50,  30,  10,
          0,  10,  20,  20,  20,  20,  10,   0,
        // Bishops
         30,  40,  40,  40,  40,  40,  40,  30,
         40,  50,  50,  50,  50,  50,  50,  40,
         40,  50,  55,  60,  60,  55,  50,  40,
         40,  55,  55,  60,  60,  55,  55,  40,
         40,  50,  60,  60,  60,  60,  50,  40,
         40,  60,  60,  60,  60,  60,  60,  40,
         40,  55,  50,  50,  50,  50,  55,  40,
         30,  40,  40,  40,  40,  40,  40,  30,
        // Rooks
         55,  55,  55,  55,  55,  55,  50,  55,
         55,  60,  60,  60,  60,  60,  60,  55,
         45,  50,  50,  50,  50,  50,  50,  45,
         45,  50,  50,  50,  50,  50,  50,  45,
         45,  50,  50,  50,  50,  50,  50,  45,
         45,  50,  50,  50,  50,  50,  50,  45,
         45,  50,  50,  50,  50,  50,  50,  45,
         50,  50,  50,  55,  55,  50,  50,  50,
        // Queens
         30,  40,  40,  45,  45,  40,  40,  30,
         40,  50,  50,  50,  50,  50,  50,  40,
         40,  50,  55,  55,  55,  55,  50,  40,
         45,  50,  55,  55,  55,  55,  50,  45,
         50,  50,  55,  55,  55,  55,  50,  45,
         40,  55,  55,  55,  55,  55,  50,  40,
         40,  50,  55,  50,  50,  50,  50,  40,
         30,  40,  40,  45,  45,  40,  40,  30,
        // Kings
         20,  10,  10,   0,   0,  10,  10,  20,
         20,  10,  10,   0,   0,  10,  10,  20,
         20,  10,  10,   0,   0,  10,  10,  20,
         20,  10,  10,   0,   0,  10,  10,  20,
         30,  20,  20,  10,  10,  20,  20,  30,
         40,  30,  30,  30,  30,  30,  30,  40,
         70,  70,  50,  50,  50,  50,  70,  70,
         70,  80,  60,  50,  50,  60,  80,  70,
    };

    Timer timer;

    int maxThinkTime = 1000;
    int thinkTime = 1000;

    Move bestMove;

    int searchCount = 0;

    public int StaticEvaluation(Board board)
    {
        int evaluation = 0;

        PieceList[] pieceLists = board.GetAllPieceLists();

        for (int i = 0; i < 6; i++)
        {
            evaluation += pieceValues[i + 1] * pieceLists[i].Count;
            evaluation -= pieceValues[i + 1] * pieceLists[i + 6].Count;

            for (int j = 0; j < pieceLists[i].Count; j++)
            {
                Piece piece = pieceLists[i].GetPiece(j);
                evaluation += pieceSquareTable[(64 * i) + 63 - piece.Square.Index] - 50;
            }
            for (int j = 0; j < pieceLists[i + 6].Count; j++)
            {
                Piece piece = pieceLists[i + 6].GetPiece(j);
                evaluation -= pieceSquareTable[(64 * i) + piece.Square.Index] - 50;
            }
        }

        return board.IsWhiteToMove ? evaluation : -evaluation;
    }

    public Move GetMove(ref Span<Move> moves, ref Span<int> moveScores, int startIndex)
    {
        for (int i = startIndex + 1; i < moves.Length; i++)
        {
            if (moveScores[i] > moveScores[startIndex])
            {
                Move betterMove = moves[i];
                moves[i] = moves[startIndex];
                moves[startIndex] = betterMove;

                int betterMoveScore = moveScores[i];
                moveScores[i] = moveScores[startIndex];
                moveScores[startIndex] = betterMoveScore;
            }
        }

        return moves[startIndex];
    }

    public void GetMoveScores(ref Span<Move> moves, ref Span<int> moveScores)
    {
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            int moveScore = 0;

            if (move.CapturePieceType != PieceType.None)
            {
                moveScore += 10 * pieceValues[(int)move.CapturePieceType] - pieceValues[(int)move.MovePieceType];
            }

            moveScores[i] = moveScore;
        }
    }

    public int SearchCaptures(Board board, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > thinkTime)
            return 0;

        int evaluation = StaticEvaluation(board);
        if (evaluation > beta)
            return beta;
        alpha = Math.Max(alpha, evaluation);

        // Allocate array of moves on the stack
        System.Span<Move> moves = stackalloc Move[256];
        // Generate legal moves
        board.GetLegalMovesNonAlloc(ref moves, capturesOnly: true);

        // Allocate array of move scores on the stack
        System.Span<int> moveScores = stackalloc int[256];
        // Generate estimated scores for each move
        GetMoveScores(ref moves, ref moveScores);

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = GetMove(ref moves, ref moveScores, i);
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(move);
            int eval = -SearchCaptures(board, -beta, -alpha);
            board.UndoMove(move);

            if (timer.MillisecondsElapsedThisTurn > thinkTime)
                return 0;

            if (eval >= beta)
                return beta;
            alpha = Math.Max(alpha, evaluation);
        }

        return alpha;
    }

    public int Search(Board board, int depth, int plyFromRoot, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > thinkTime)
            return 0;

        // Check for checkmate
        if (board.IsInCheckmate())
            return -(20000 - plyFromRoot); // Checkmate
        else if (board.IsDraw())
            return 0; // Stalemate or draw

        // Search depth reached, return static evaluation of the current position
        if (depth == 0)
            return StaticEvaluation(board); //SearchCaptures(board, alpha, beta);

        // Allocate array of moves on the stack
        System.Span<Move> moves = stackalloc Move[256];
        // Generate legal moves
        board.GetLegalMovesNonAlloc(ref moves);

        // Allocate array of move scores on the stack
        System.Span<int> moveScores = stackalloc int[256];
        // Generate estimated scores for each move
        GetMoveScores(ref moves, ref moveScores);

        if (plyFromRoot == 0 && bestMove != Move.NullMove)
        {
            int bestMoveIndex = moves.IndexOf(bestMove);
            moveScores[bestMoveIndex] = 20000;
        }

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = GetMove(ref moves, ref moveScores, i);
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(move);
            int eval = -Search(board, depth - 1, plyFromRoot + 1, -beta, -alpha);
            board.UndoMove(move);

            if (timer.MillisecondsElapsedThisTurn > thinkTime)
                return 0;

            if (eval >= beta)
            {
                return beta;
            }

            if (eval > alpha)
            {
                alpha = eval;
                if (plyFromRoot == 0)
                    bestMove = move;
            }
        }

        return alpha;
    }

    public Move Think(Board board, Timer t)
    {
        timer = t;
        bestMove = Move.NullMove;

        thinkTime = Math.Clamp((timer.MillisecondsRemaining - 1000) / 10, 100, 2000);

        for (int searchDepth = 1; searchDepth <= int.MaxValue; searchDepth++)
        {
            if (timer.MillisecondsElapsedThisTurn > thinkTime)
                break;

            Search(board, searchDepth, 0, -1000000, 1000000);
        }

        return bestMove;
    }
}