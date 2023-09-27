using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    // Values of pieces: none, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 320, 328, 500, 900, 20000 };

    // Encoded piece-square table
    // Values are encoded in 64-bit values
    // Each hex-digit represents a score for a piece
    // on a particular square, normalized between 0-15,
    // where 8 is neutral (0), F is a very positive score,
    // and 0 is a very negative score.
    ulong[] pieceSquareTableEncoded = {
        // Pawns
        0x888888889AA55AA9,
        0x888BB88897688679,
        0xAABDDBAA99ACCA99,
        0x88888888FFFFFFFF,
        // Knights
        0x1589985101333310,
        0x38ABBA8339AAAA93,
        0x38AAAA8339ABBA93,
        0x0133331015888851,
        // Bishops
        0x6988889656666665,
        0x68AAAA866AAAAAA6,
        0x689AA986699AA996,
        0x6566666656888888,
        // Rooks
        0x7888888788888888,
        0x7888888778888887,
        0x7888888778888887,
        0x999999999AAAAAA9,
        // Queens
        0x6898888656677665,
        0x8899998769999986,
        0x6899998678999987,
        0x5667766568888886,
        // Kings
        0xBB8888BBBDA88ADB,
        0x5331133565555556,
        0x3110011331100113,
        0x3110011331100113,
    };

    // Transposition table entry, fits in 16 bytes
    private struct TTableEntry
    {
        public ulong ZobristKey;
        public Move BestMove;
        public short Depth;
        public short Evaluation;
    }

    int[] pieceSquareTable = new int[768];

    ulong ttMaxEntries = 0x800000; // #DEBUG
    int ttEntries = 0; // #DEBUG
    int ttOverwrites = 0; // #DEBUG
    TTableEntry[] transpositionTable;

    int lookupCount; // #DEBUG
    int searchCount; // #DEBUG
    int betaCutoffCount; // #DEBUG
    int evaluationCount; // #DEBUG

    Timer timer;
    int thinkTime;
    //int optimumTime;

    //int totalBestMoveChanges;

    int searchDepth;

    int bestEval; // #DEBUG
    Move bestMove;

    public MyBot()
    {
        // Allocate transposition table (8,388,608 (0x800000) * 16 bytes = 134.2 MB)
        transpositionTable = new TTableEntry[0x800000];

        for (int i = 0; i < 384; i++)
            pieceSquareTable[i] = (int)(pieceSquareTableEncoded[i / 16] >> i % 16 * 4 & 0xF) * 8 - 64;
        for (int i = 0; i < 384; i++)
            pieceSquareTable[i + 384] = pieceSquareTable[(i & 0xFFFFFFC0) + (i % 64 ^ 56)];
    }

    private ref TTableEntry GetTableEntry(Board board)
    {
        return ref transpositionTable[board.ZobristKey & 0x7FFFFF];
    }

    public void StoreEvaluation(Board board, Move move, int depth, int eval)
    {
        ref TTableEntry tableEntry = ref GetTableEntry(board);
        //if (tableEntry.Depth == 0) ttEntries++; else ttOverwrites++; // #DEBUG
        tableEntry.BestMove = move;
        tableEntry.ZobristKey = board.ZobristKey;
        tableEntry.Depth = (short)depth;
        tableEntry.Evaluation = (short)eval;
    }

    public int StaticEvaluation(Board board)
    {
        //evaluationCount++; // #DEBUG
        int evaluation = 0;

        PieceList[] pieceLists = board.GetAllPieceLists();

        for (int i = 0; i < 12; i++)
        {
            int eval = 0;

            // Count material
            eval += pieceValues[i % 6 + 1] * pieceLists[i].Count;

            // Count PST-values
            for (int j = 0; j < pieceLists[i].Count; j++)
                eval += pieceSquareTable[64 * i + pieceLists[i].GetPiece(j).Square.Index];

            // Negate eval if black piece XOR black to move
            eval = i < 6 == board.IsWhiteToMove ? eval : -eval;

            evaluation += eval;
        }

        return evaluation;
    }

    // Get the next most promising move in the list
    public Move GetMove(ref Span<Move> moves, ref Span<int> moveScores, int startIndex, Move searchThisMoveFirst)
    {
        for (int i = startIndex; i < moves.Length; i++)
        {
            Move move = moves[i];

            if (moveScores[i] == 0)
                moveScores[i] = move == searchThisMoveFirst ? 20000 :
                                move.CapturePieceType != PieceType.None ? 10 * pieceValues[(int)move.CapturePieceType] - pieceValues[(int)move.MovePieceType] : 1;

            int score = moveScores[i];
            if (score > moveScores[startIndex])
            {
                moves[i] = moves[startIndex];
                moves[startIndex] = move;
                moveScores[i] = moveScores[startIndex];
                moveScores[startIndex] = score;
            }
        }

        return moves[startIndex];
    }

    public bool TimesUp => timer.MillisecondsElapsedThisTurn > thinkTime;

    // Quiescence search over capture moves only
    public int SearchCaptures(Board board, int alpha, int beta)
    {
        //if (TimesUp)
        //    return 0;

        int eval = StaticEvaluation(board);
        if (eval > beta)
            return beta;
        alpha = Math.Max(alpha, eval);

        // Allocate array of moves on the stack
        Span<Move> moves = stackalloc Move[256];
        // Allocate array of move scores on the stack
        Span<int> moveScores = stackalloc int[256];
        //// Generate legal moves
        board.GetLegalMovesNonAlloc(ref moves, true);
        // Generate estimated scores for each move
        //GetMoveScores(ref moves, ref moveScores, Move.NullMove);

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = GetMove(ref moves, ref moveScores, i, Move.NullMove);
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(move);
            eval = -SearchCaptures(board, -beta, -alpha);
            board.UndoMove(move);

            if (TimesUp || eval >= beta)
                return beta;
            alpha = Math.Max(alpha, eval);
        }

        return alpha;
    }

    public int Search(Board board, int depth, int alpha, int beta)
    {
        //if (TimesUp)
        //    return 0;

        //searchCount++; // #DEBUG
        // Check for checkmate
        if (board.IsInCheckmate())
            return -(20000 + depth * 4); // Checkmate
        else if (board.IsDraw())
            return 0; // Stalemate or draw

        TTableEntry tableEntry = GetTableEntry(board);
        int eval = tableEntry.Evaluation;

        if (depth != searchDepth && tableEntry.ZobristKey == board.ZobristKey && tableEntry.Depth >= depth)
        {
            int flag = eval & 3;
            if (flag == 0b00)
                return eval;
            else if (flag == 0b01 && eval >= beta)
                return beta;
            else if (eval <= alpha)
                return alpha;
        }

        // Search depth reached, return static evaluation of the current position
        if (depth == 0)
            return SearchCaptures(board, alpha, beta);

        Move currentBestMove = tableEntry.ZobristKey == board.ZobristKey ? tableEntry.BestMove : Move.NullMove;

        // Allocate array of moves on the stack
        System.Span<Move> moves = stackalloc Move[256];
        // Allocate array of move scores on the stack
        System.Span<int> moveScores = stackalloc int[256];
        // Generate legal moves
        board.GetLegalMovesNonAlloc(ref moves);
        // Generate estimated scores for each move
        //GetMoveScores(ref moves, ref moveScores, currentBestMove);

        // Search all moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = GetMove(ref moves, ref moveScores, i, currentBestMove);
            // Make move, recursively search all responses, then undo the move
            board.MakeMove(move);
            eval = -Search(board, depth - 1, -beta, -alpha | 1); // -alpha is either EXACT or LOWER. OR with 1 forces it to LOWER.
            board.UndoMove(move);

            if (TimesUp)
                return 0;

            if (eval >= beta - 1) // beta is LOWER. Subtracting 1 turns it into EXACT.
            {
                StoreEvaluation(board, move, depth, beta);
                //betaCutoffCount++; // #DEBUG
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
        //totalBestMoveChanges = 0;

        //optimumTime = Math.Max(Math.Min(
        //    timer.MillisecondsRemaining / 40 + (timer.IncrementMilliseconds / 2),
        //    timer.MillisecondsRemaining - 500),
        //    100
        //);

        //thinkTime = optimumTime;
        thinkTime = 100;
        //thinkTime = Math.Clamp((timer.MillisecondsRemaining - 1000) / 10, 100, 2000);

        //ulong bitboard = BitboardHelper.GetPieceAttacks(PieceType.Queen, new Square(4, 3), board, true); // #DEBUG
        //BitboardHelper.VisualizeBitboard(bitboard); // #DEBUG

        ttOverwrites = 0; // #DEBUG

        for (searchDepth = 1; searchDepth <= int.MaxValue; searchDepth++)
        {
            if (TimesUp)
                break;

            lookupCount = 0; // #DEBUG
            searchCount = 0; // #DEBUG
            betaCutoffCount = 0; // #DEBUG
            evaluationCount = 0; // #DEBUG

            //bestMovePreviousIteration = bestMove;

            Search(board, searchDepth, -32765, 32765);

            //if (bestMovePreviousIteration != bestMove)
            //    totalBestMoveChanges++;

            //double bestMoveInstability = 1 + 0.2 * totalBestMoveChanges;
            //thinkTime = (int)(optimumTime * bestMoveInstability);

            //Console.WriteLine("depth: " + searchDepth + " eval: " + bestEval + " best move: " + bestMove.ToString()); // #DEBUG
            //Console.WriteLine(searchCount + " positions searched"); // #DEBUG
            //Console.WriteLine(lookupCount + " positions looked up"); // #DEBUG
            //Console.WriteLine(betaCutoffCount + " beta cut-offs performed"); // #DEBUG
            //Console.WriteLine(evaluationCount + " evaluations"); // #DEBUG
        }

        //for (int i = 0; i < 96; i++)
        //{
        //    for (int j = 0; j < 8; j++)
        //    {
        //        Console.Write(pieceSquareTable[i * 8 + j] + " ");
        //    }
        //    Console.WriteLine("");
        //}

        //Console.WriteLine("Size : " + ttMaxEntries * 16 / 1000 + "KB\nOccupancy: " + (ttEntries / (double)ttMaxEntries * 100.0) + "%\nOverwrites: " + (ttOverwrites / (double)ttMaxEntries * 100.0) + "%"); // #DEBUG

        return bestMove;
    }
}