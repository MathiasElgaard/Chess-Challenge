using ChessChallenge.API;
using System;
using System.Numerics;

public class MyBot : IChessBot
{
    private struct TTableEntry
    {
        public ulong ZobristKey;
        public Move BestMove;
        public int Depth;
        public int Evaluation;
    }

    // Values of pieces: none, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 20000 };

    // Encoded piece-square table
    ulong[] pieceSquareTableEncoded = {
        // Pawns
        0xFFFFFFFF88888888,
        0x99ACCA99AABDDBAA,
        0x99688679888BB888,
        0x888888889AA55AA9,
        // Knights
        0x1588885101333310,
        0x39ABBA9338AAAA83,
        0x39AAAA9338ABBA83,
        0x0133331015899851,
        // Bishops
        0x6888888656666665,
        0x699AA996689AA986,
        0x6AAAAAA668AAAA86,
        0x5666666569888896,
        // Rooks
        0x9AAAAAA999999999,
        0x7888888778888887,
        0x7888888778888887,
        0x8888888878888887,
        // Queens
        0x6888888656677665,
        0x7899998768999986,
        0x6999998688999987,
        0x5667766568988886,
        // Kings
        0x3110011331100113,
        0x3110011331100113,
        0x6555555653311335,
        0xBDA88ADBBB8888BB,
    };

    int[] pieceSquareTable = new int[384];

    ulong ttMaxEntries = 800000;
    int ttEntries = 0;
    TTableEntry[] transpositionTable;

    int lookupCount; // #DEBUG
    int searchCount; // #DEBUG
    int betaCutoffCount; // #DEBUG

    Timer timer;
    int thinkTime;
    //int optimumTime;

    //int totalBestMoveChanges;

    int searchDepth;

    int bestEval;
    Move bestMove;

    public MyBot()
    {
        transpositionTable = new TTableEntry[ttMaxEntries];

        for (int i = 0; i < 384; i++)
        {
            pieceSquareTable[i] = (int)(pieceSquareTableEncoded[i / 16] >> i % 16 * 4 & 0xF) * 8 - 64;
        }
    }

    private ref TTableEntry GetTableEntry(Board board)
    {
        return ref transpositionTable[board.ZobristKey % ttMaxEntries];
    }

    public void StoreEvaluation(Board board, Move move, int depth, int eval)
    {
        ref TTableEntry tableEntry = ref GetTableEntry(board);
        tableEntry.BestMove = move;
        tableEntry.ZobristKey = board.ZobristKey;
        tableEntry.Depth = depth;
        tableEntry.Evaluation = eval;
    }

    public int StaticEvaluation(Board board)
    {
        int evaluation = 0;

        PieceList[] pieceLists = board.GetAllPieceLists();

        //double phase = 24;
        //for (int i = 0; i < 6; i++)
        //{
        //    phase -= phaseValues[i + 1] * pieceLists[i].Count;
        //    phase -= phaseValues[i + 1] * pieceLists[i + 6].Count;
        //}
        //phase /= 24.0;

        for (int i = 0; i < 6; i++)
        {
            evaluation += pieceValues[i + 1] * pieceLists[i].Count;
            evaluation -= pieceValues[i + 1] * pieceLists[i + 6].Count;

            for (int j = 0; j < pieceLists[i].Count; j++)
            {
                Piece piece = pieceLists[i].GetPiece(j);
                evaluation += pieceSquareTable[(64 * i) + 63 - piece.Square.Index];

                //if (piece.PieceType == PieceType.King)
                //    pieceSquareValue = (int)((pieceSquareValue * (1.0 - phase)) + ((pieceSquareTable[447 - piece.Square.Index] - 50) * phase));

                //evaluation += pieceSquareValue;

                //evaluation += BitboardHelper.GetNumberOfSetBits(BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, true));
            }
            for (int j = 0; j < pieceLists[i + 6].Count; j++)
            {
                Piece piece = pieceLists[i + 6].GetPiece(j);
                evaluation -= pieceSquareTable[(64 * i) + piece.Square.Index];

                //if (piece.PieceType == PieceType.King)
                //    pieceSquareValue = (int)((pieceSquareValue * (1.0 - phase)) + ((pieceSquareTable[364 + piece.Square.Index] - 50) * phase));

                //evaluation -= pieceSquareValue;

                //evaluation -= BitboardHelper.GetNumberOfSetBits(BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, false));
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

    public void GetMoveScores(ref Span<Move> moves, ref Span<int> moveScores, Move searchThisMoveFirst)
    {
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            int moveScore = 0;

            if (move == searchThisMoveFirst)
            {
                moveScore = 20000;
            }
            else if (move.CapturePieceType != PieceType.None)
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

        int eval = StaticEvaluation(board);
        if (eval > beta)
            return beta;
        alpha = Math.Max(alpha, eval);

        // Allocate array of moves on the stack
        System.Span<Move> moves = stackalloc Move[256];
        // Generate legal moves
        board.GetLegalMovesNonAlloc(ref moves, capturesOnly: true);

        // Allocate array of move scores on the stack
        System.Span<int> moveScores = stackalloc int[256];
        // Generate estimated scores for each move
        GetMoveScores(ref moves, ref moveScores, Move.NullMove);

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = GetMove(ref moves, ref moveScores, i);
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(move);
            eval = -SearchCaptures(board, -beta, -alpha);
            board.UndoMove(move);

            if (timer.MillisecondsElapsedThisTurn > thinkTime)
                return 0;

            if (eval >= beta)
                return beta;
            alpha = Math.Max(alpha, eval);
        }

        return alpha;
    }

    public int Search(Board board, int depth, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > thinkTime)
            return 0;

        //alpha = ((alpha + 1 & ~3) + 1);
        //beta = ((beta + 1 & ~3) - 1);
        searchCount++; // #DEBUG
        // Check for checkmate
        if (board.IsInCheckmate())
            return -(20000 + depth * 4); // Checkmate
        else if (board.IsDraw())
            return 0; // Stalemate or draw

        TTableEntry tableEntry = GetTableEntry(board);

        if (depth != searchDepth && tableEntry.ZobristKey == board.ZobristKey && tableEntry.Depth >= depth)
        {
            //if (tableEntry.EvalType == 0)
            //{
            //    lookupCount++; // #DEBUG
            //    return tableEntry.Evaluation;
            //}
            //else if (tableEntry.EvalType == 3 && tableEntry.Evaluation <= alpha)
            //{
            //    lookupCount++; // #DEBUG
            //    return alpha;
            //}
            //else if (tableEntry.EvalType == 1 && tableEntry.Evaluation >= beta)
            //{
            //    lookupCount++; // #DEBUG
            //    return beta;
            //}
            int flag = tableEntry.Evaluation & 3;
            if (flag == 0b00)
            {
                lookupCount++; // #DEBUG
                return tableEntry.Evaluation;
            }
            else if (flag == 0b01 && tableEntry.Evaluation >= beta)
            {
                lookupCount++; // #DEBUG
                return beta;
            }
            else if (tableEntry.Evaluation <= alpha)
            {
                lookupCount++; // #DEBUG
                return alpha;
            }
        }

        // Search depth reached, return static evaluation of the current position
        if (depth == 0)
            return SearchCaptures(board, alpha, beta);

        // Allocate array of moves on the stack
        System.Span<Move> moves = stackalloc Move[256];
        // Generate legal moves
        board.GetLegalMovesNonAlloc(ref moves);

        Move searchThisMoveFirst = tableEntry.ZobristKey == board.ZobristKey ? tableEntry.BestMove : Move.NullMove;

        // Allocate array of move scores on the stack
        System.Span<int> moveScores = stackalloc int[256];
        // Generate estimated scores for each move
        GetMoveScores(ref moves, ref moveScores, searchThisMoveFirst);

        Move currentBestMove = Move.NullMove;

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = GetMove(ref moves, ref moveScores, i);
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(move);
            int eval = -Search(board, depth - 1, -beta, -alpha | 1); // -alpha is either EXACT or LOWER. OR with 1 forces it to LOWER.
            board.UndoMove(move);

            if (timer.MillisecondsElapsedThisTurn > thinkTime)
                return 0;

            if (eval >= beta - 1) // beta is LOWER. Subtracting 1 turns it into EXACT.
            {
                StoreEvaluation(board, move, depth, beta);
                betaCutoffCount++; // #DEBUG
                return beta;
            }

            if (eval > alpha)
            {
                alpha = eval;
                currentBestMove = move;
                if (depth == searchDepth)
                {
                    bestEval = eval; // #DEBUG
                    bestMove = move;
                }
            }
        }

        StoreEvaluation(board, currentBestMove, depth, alpha);
        return alpha;
    }

    public Move Think(Board board, Timer t)
    {
        timer = t;
        bestEval = 0; // #DEBUG
        bestMove = Move.NullMove;
        //totalBestMoveChanges = 0;

        //optimumTime = Math.Max(Math.Min(
        //    timer.MillisecondsRemaining / 40 + (timer.IncrementMilliseconds / 2),
        //    timer.MillisecondsRemaining - 500),
        //    100
        //);

        //thinkTime = optimumTime;
        thinkTime = 1500;
        //thinkTime = Math.Clamp((timer.MillisecondsRemaining - 1000) / 10, 100, 2000);

        //ulong bitboard = BitboardHelper.GetPieceAttacks(PieceType.Queen, new Square(4, 3), board, true); // #DEBUG
        //BitboardHelper.VisualizeBitboard(bitboard); // #DEBUG

        for (searchDepth = 1; searchDepth <= int.MaxValue; searchDepth++)
        {
            if (timer.MillisecondsElapsedThisTurn > thinkTime)
                break;

            lookupCount = 0; // #DEBUG
            searchCount = 0; // #DEBUG
            betaCutoffCount = 0; // #DEBUG

            //bestMovePreviousIteration = bestMove;

            Search(board, searchDepth, -32765, 32765);

            //if (bestMovePreviousIteration != bestMove)
            //    totalBestMoveChanges++;

            //double bestMoveInstability = 1 + 0.2 * totalBestMoveChanges;
            //thinkTime = (int)(optimumTime * bestMoveInstability);

            Console.WriteLine("depth: " + searchDepth + " eval: " + bestEval + " best move: " + bestMove.ToString()); // #DEBUG
            Console.WriteLine(searchCount + " positions searched"); // #DEBUG
            Console.WriteLine(lookupCount + " positions looked up"); // #DEBUG
            Console.WriteLine(betaCutoffCount + " beta cut-offs performed"); // #DEBUG
        }

        //Console.WriteLine("eval: " + bestEval + " best move: " + bestMove.ToString()); // #DEBUG

        //unsafe
        //{
        //    Console.WriteLine(sizeof(TTableEntry)); // #DEBUG
        //}


        //Console.WriteLine("Occupancy: " + (ttEntries / (double)ttMaxEntries * 100.0) + "%"); // #DEBUG

        return bestMove;
    }
}