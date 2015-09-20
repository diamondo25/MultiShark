#region License
/* **********************************************************************************
 * Copyright (c) Roman Ivantsov
 * This source code is subject to terms and conditions of the MIT License
 * for Irony. A copy of the license can be found in the License.txt file
 * at the root of this distribution. 
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the 
 * MIT License.
 * You must not remove this notice from this software.
 * **********************************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.Text;

namespace Irony.Compiler {

  //Scanner class. The Scanner's function is to transform a stream of characters into bigger aggregates/words or lexemes, 
  // like identifier, number, literal, etc. 

  public class Scanner  {
    public Scanner(GrammarData data)  {
      _data = data;
      _lineTerminators = _data.Grammar.LineTerminators.ToCharArray();
    }

    #region Fields: _data, _source, _context, _caseSensitive, _currentToken
    GrammarData _data;
    ISourceStream  _source;
    CompilerContext  _context;
    char[] _lineTerminators;
    bool _caseSensitive;
    Token _currentToken; //it is used only in BeginScan iterator, but we define it as a field to avoid creating local state in iterator
    //buffered tokens can come from expanding a multi-token, when Terminal.TryMatch() returns several tokens packed into one token
    TokenList _bufferedTokens = new TokenList();
    #endregion

    #region Events: TokenCreated
    //Note that scanner's output stream may not contain all tokens received by parser. Additional tokens
    // may be generated by intermediate token filters. To listen to token stream at parser input, 
    // use Parser's TokenReceived event. 
    public event EventHandler<TokenEventArgs> TokenCreated;
    TokenEventArgs _tokenArgs = new TokenEventArgs(null);

    protected void OnTokenCreated(Token token) {
      if (TokenCreated == null) return;
      _tokenArgs.Token = token;
      TokenCreated(this, _tokenArgs);
    }
    #endregion

    public void Prepare(CompilerContext context, ISourceStream source) {
      _context = context;
      _caseSensitive = context.Compiler.Grammar.CaseSensitive;
      _source = source;
      _currentToken = null;
      _bufferedTokens.Clear();
      ResetSource();
    }

    //Use this method in real compiler, in iterator-connected pipeline
    public IEnumerable<Token> BeginScan() {
      //We don't do "while(!_source.EOF())... because on EOF() we need to continue and produce EOF token 
      //  and then do "yield break" - see below
      while (true) {  
        _currentToken = ReadToken();
        if (TokenCreated != null)
          OnTokenCreated(_currentToken);
        //if (tkn.Terminal.Category != TerminalCategory.Comment)
        yield return _currentToken;
        if (_currentToken.Terminal == Grammar.Eof)
          yield break;
      }//while
    }// method

    //Use this method for VS integration; VS language package requires scanner that returns tokens one-by-one. 
    // Start and End positions required by this scanner may be derived from Token : 
    //   start=token.Location.Position; end=start + token.Length;
    // state is not used now - maybe in the future
    public Token GetNext(ref int state) {
      return ReadToken();
    }

    private Token ReadToken() {
      if (_bufferedTokens.Count > 0) {
        Token tkn = _bufferedTokens[0];
        _bufferedTokens.RemoveAt(0);
        return tkn; 
      }
      //1. Skip whitespace. We don't need to check for EOF: at EOF we start getting 0-char, so we'll get out automatically
      while (_data.Grammar.WhitespaceChars.IndexOf(_source.CurrentChar) >= 0)
        _source.Position++;
      //That's the token start, calc location (line and column)
      SetTokenStartLocation();
      //Check for EOF
      if (_source.EOF())
        return Token.Create (Grammar.Eof, _context, _source.TokenStart, string.Empty, Grammar.Eof.Name);
      //Find matching terminal
      // First, try terminals with explicit "first-char" prefixes, selected by current char in source
      TerminalList terms = SelectTerminals(_source.CurrentChar);
      Token result = MatchTerminals(terms);
      //If no token, try FallbackTerminals
      if (result == null && _data.FallbackTerminals.Count > 0)
        result = MatchTerminals(_data.FallbackTerminals); 
      //If we don't have a token from registered terminals, try Grammar's method
      if (result == null) 
        result = _data.Grammar.TryMatch(_context, _source);
      //Check if we have a multi-token; if yes, copy all but first child tokens from ChildNodes to _bufferedTokens, 
      //  and set result to the first child token
      if (result != null && result.IsMultiToken()) {
        foreach (Token tkn in result.ChildNodes)
          _bufferedTokens.Add(tkn);
        result = _bufferedTokens[0];
        _bufferedTokens.RemoveAt(0);
      }
      //If we have normal token then return it
      if (result != null && !result.IsError()) {
        //restore position to point after the result token
        _source.Position = _source.TokenStart.Position + result.Length; 
        return result;
      } 
      //we have an error: either error token or no token at all
      if (result == null) //if no error result then create it
        result = Grammar.CreateSyntaxErrorToken(_context, _source.TokenStart, "Invalid character: '{0}'", _source.CurrentChar);
      Recover();
      return result;
    }//method

    private Token MatchTerminals(TerminalList terminals) {
      Token result = null;
      foreach (Terminal term in terminals) {
        // Check if the term has lower priority that result token we already have; 
        //  if term.Priority is lower then we don't need to check anymore, higher priority wins
        // Note that terminals in the list are sorted in descending priority order
        if (result != null && result.Terminal.Priority > term.Priority)
          break;
        //Reset source position and try to match
        _source.Position = _source.TokenStart.Position;
        Token token = term.TryMatch(_context, _source);
        //Take this token as result only if we don't have anything yet, or if it is longer token than previous
        if (token != null && (token.IsError() || result == null || token.Length > result.Length))
          result = token;
        if (result != null && result.IsError()) break;
      }
      return result; 
    }

    private TerminalList SelectTerminals(char current) {
      TerminalList result;
      if (!_caseSensitive)
        current = char.ToLower(current);
      if (_data.TerminalsLookup.TryGetValue(current, out result))
        return result;
      else
        return _data.FallbackTerminals;
    }//Select

    private void Recover() {
      while (!_source.EOF() && _data.ScannerRecoverySymbols.IndexOf(_source.CurrentChar) < 0)
        _source.Position++;
    }

    public override string ToString() {
      return _source.ToString(); //show 30 chars starting from current position
    }

    #region TokenStart calculations
    private int _nextNewLinePosition = -1; //private field to cache position of next \n character
    public void ResetSource() {
      _source.Position = 0;
      _source.TokenStart = new SourceLocation();
      _nextNewLinePosition = _source.Text.IndexOf('\n');
    }

    //Calculates the _source.TokenStart values (row/column) for the token which starts at the current position.
    // We just skipped the whitespace and about to start scanning the next token.
    private static char[] _tab_arr = { '\t' };
    internal void SetTokenStartLocation() {
      //cache values in local variables
      SourceLocation tokenStart = _source.TokenStart;
      int newPosition = _source.Position;
      string text = _source.Text;

      // Currently TokenStart field contains location (pos/line/col) of the last created token. 
      // First, check if new position is in the same line; if so, just adjust column and return
      //  Note that this case is not line start, so we do not need to check tab chars (and adjust column) 
      if (newPosition <= _nextNewLinePosition || _nextNewLinePosition < 0) {
        tokenStart.Column += newPosition - tokenStart.Position;
        tokenStart.Position = newPosition;
        _source.TokenStart = tokenStart;
        return;
      }
      //So new position is on new line (beyond _nextNewLinePosition)
      //First count \n chars in the string fragment
      int lineStart = _nextNewLinePosition;
      int nlCount = 1; //we start after old _nextNewLinePosition, so we count one NewLine char
      CountCharsInText(text, _lineTerminators, lineStart + 1, newPosition - 1, ref nlCount, ref lineStart);
      tokenStart.Line += nlCount;
      //at this moment lineStart is at start of line where newPosition is located 
      //Calc # of tab chars from lineStart to newPosition to adjust column#
      int tabCount = 0;
      int dummy = 0;
      if (_source.TabWidth > 1)
        CountCharsInText(text, _tab_arr, lineStart, newPosition - 1, ref tabCount, ref dummy);

      //adjust TokenStart with calculated information
      tokenStart.Position = newPosition;
      tokenStart.Column = newPosition - lineStart - 1;
      if (tabCount > 0)
        tokenStart.Column += (_source.TabWidth - 1) * tabCount; // "-1" to count for tab char itself

      //finally cache new line and assign TokenStart
      _nextNewLinePosition = text.IndexOfAny(_lineTerminators, newPosition);
      _source.TokenStart = tokenStart;
    }

    private void CountCharsInText(string text, char[] chars, int from, int until, ref int count, ref int lastPosition) {
      if (from > until) return;
      while (true) {
        int next = text.IndexOfAny(chars, from, until - from + 1);
        if (next < 0) return;
        //CR followed by LF is one line terminator, not two; we put it here, just to cover for special case; it wouldn't break
        // the case when this function is called to count tabs
        bool isCRLF = (text[next] == '\n' && next > 0 && text[next - 1] == '\r');
        if (!isCRLF)
          count++; //count
        lastPosition = next;
        from = next + 1;
      }

    }
    #endregion


  }//class

}//namespace