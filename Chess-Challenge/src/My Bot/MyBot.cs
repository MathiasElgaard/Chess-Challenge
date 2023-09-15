using ChessChallenge.API;
using System;
using System.Numerics;

public class MyBot : IChessBot
{
    // Values of pieces: none, pawn, knight, bishop, rook, queen, king
    public readonly int[] pieceValues = { 0, 100, 300, 300, 500, 900, 20000 };

    // Encoded piece-square table
    public readonly ulong[] pieceSquareTableEncoded = {
        // Pawns
        0x88888888,
        0xFFFFFFFF,
        0xAABDDBAA,
        0x99ACCA99,
        0x888BB888,
        0x99688679,
        0x9AA55AA9,
        0x88888888,
        // Knights
        0x01333310,
        0x15888851,
        0x38AAAA83,
        0x39ABBA93,
        0x38ABBA83,
        0x39AAAA93,
        0x15899851,
        0x01333310,
        // Bishops
        0x56666665,
        0x68888886,
        0x689AA986,
        0x699AA996,
        0x68AAAA86,
        0x6AAAAAA6,
        0x69888896,
        0x56666665,
        // Rooks
        0x99999999,
        0x9AAAAAA9,
        0x78888887,
        0x78888887,
        0x78888887,
        0x78888887,
        0x78888887,
        0x88888888,
        // Queens
        0x56677665,
        0x68888886,
        0x68999986,
        0x78999987,
        0x88999987,
        0x69999986,
        0x68988886,
        0x56677665,
        // Kings
        0x31100113,
        0x31100113,
        0x31100113,
        0x31100113,
        0x53311335,
        0x65555556,
        0xBB8888BB,
        0xBDA88ADB,
    };

    public int[] pieceSquareTable = new int[384];

    Timer timer;
    int thinkTime = 1000;

    Move bestMove;

    int searchCount = 0;

    public MyBot()
    {
        for (int i = 0; i < 384; i++)
        {
            pieceSquareTable[i] = (int)((pieceSquareTableEncoded[i / 8] >> ((i % 8) * 4)) & 15ul);
        }
    }

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
            return (plyFromRoot - 20000); // Checkmate
        else if (board.IsDraw())
            return 0; // Stalemate or draw

        // Search depth reached, return static evaluation of the current position
        if (depth == 0)
            return SearchCaptures(board, alpha, beta);

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