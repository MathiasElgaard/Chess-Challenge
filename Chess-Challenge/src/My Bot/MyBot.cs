using ChessChallenge.API;
using System;
using System.Numerics;

public class MyBot : IChessBot
{
    // Values of pieces: none, pawn, knight, bishop, rook, queen, king
    public readonly int[] pieceValues = { 0, 100, 300, 300, 500, 900, 20000 };

    public readonly int[] pieceSquareTable = {
        // Pawns
          0,  0,  0,  0,  0,  0,  0,  0,
         50, 50, 50, 50, 50, 50, 50, 50,
         10, 10, 20, 30, 30, 20, 10, 10,
          5,  5, 10, 25, 25, 10,  5,  5,
          0,  0,  0, 20, 20,  0,  0,  0,
          5, -5,-10,  0,  0,-10, -5,  5,
          5, 10, 10,-20,-20, 10, 10,  5,
          0,  0,  0,  0,  0,  0,  0,  0,
        // Knights
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50,
        // Bishops
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
        // Rooks
          5,  5,  5,  5,  5,  5,  0,  5,
          5, 10, 10, 10, 10, 10, 10,  5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          0,  0,  2,  5,  5,  2,  0,  0,
        // Queens
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20,
        // Kings
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20,
    };

    Timer timer;

    int maxThinkTime = 1000;
    int thinkTime;

    int bestMoveEval;
    Move bestMove;

    int searchCount;

    public MyBot()
    {
        thinkTime = maxThinkTime;
        searchCount = 0;
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
                evaluation += pieceSquareTable[(64 * i) + 63 - piece.Square.Index];
            }
            for (int j = 0; j < pieceLists[i + 6].Count; j++)
            {
                Piece piece = pieceLists[i + 6].GetPiece(j);
                evaluation -= pieceSquareTable[(64 * i) + piece.Square.Index];
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

        searchCount++;

        int evaluation = StaticEvaluation(board);
        if (evaluation > beta)
            return beta;
        alpha = Math.Max(alpha, evaluation);

        // // Check for checkmate
        // if (board.IsInCheckmate())
        // {
        //     return -(20000 - plyFromRoot); // Checkmate
        // }
        // else if (board.IsDraw())
        // {
        //     return 0; // Stalemate or draw
        // }

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
            {
                return beta;
            }
            alpha = Math.Max(alpha, evaluation);
        }

        return alpha;
    }

    public int Search(Board board, int depth, int plyFromRoot, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > thinkTime)
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
            return SearchCaptures(board, alpha, beta);
        }

        //Move[] moves = plyFromRoot == 0 ? bestMoves : board.GetLegalMoves();

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
            // moves[bestMoveIndex] = moves[0];
            // moves[0] = bestMove;
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
                {
                    bestMoveEval = eval;
                    bestMove = move;
                }
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

            searchCount = 0;
            bestMoveEval = Search(board, searchDepth, 0, -1000000, 1000000);
            // Console.WriteLine("depth: " + searchDepth + ", best move: " + bestMove.ToString());
        }
        // Console.WriteLine("Searched " + searchCount + " positions");
        // Console.WriteLine("Zobrist: " + board.ZobristKey);

        return bestMove;
    }
}