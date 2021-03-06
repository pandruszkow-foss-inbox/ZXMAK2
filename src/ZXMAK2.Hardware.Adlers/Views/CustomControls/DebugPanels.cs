using System;
using System.Windows.Forms;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace ZXMAK2.Hardware.Adlers.Views.CustomControls
{
   public class DasmPanel : Control
   {
      public DasmPanel()
      {
         TabStop = true;
         //         BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
         this.Font = new Font("Courier", 13,System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
         Size = new System.Drawing.Size(424, 148);
         ControlStyles styles = ControlStyles.Selectable |
                                ControlStyles.UserPaint |
                                ControlStyles.ResizeRedraw |
                                ControlStyles.StandardClick |           // csClickEvents
                                ControlStyles.UserMouse |               // csCaptureMouse
                                ControlStyles.ContainerControl |        // csAcceptsControls?
                                ControlStyles.StandardDoubleClick |     // csDoubleClicks
//                                ControlStyles.Opaque  |                 // csOpaque
                                0;
         base.SetStyle(styles, true);

         mouseTimer = new Timer();
         mouseTimer.Enabled = false;
         mouseTimer.Interval = 50;
         mouseTimer.Tick += OnMouseTimer;

         fLineHeight = 1;
         fVisibleLineCount = 0;
         fTopAddress = 0;
         fActiveLine = 0;
         fBreakColor = Color.Red;
         fBreakForeColor = Color.Black;
         UpdateLines();
      }

      public Color BreakpointColor
      {
          get { return fBreakColor; }
          set { fBreakColor = value; Refresh(); }
      }
      public Color BreakpointForeColor
      {
          get { return fBreakForeColor; }
          set { fBreakForeColor = value; Refresh(); }
      }
      public ushort TopAddress
      {
          get { return fTopAddress; }
          set
          {
              fTopAddress = value;
              fActiveLine = 0;
              UpdateLines();
              Invalidate();
          }
      }
      public ushort ActiveAddress
      {
          get { if ((fActiveLine >= 0) && (fActiveLine < fLineCount)) return fADDRS[fActiveLine]; return 0; }
          set
          {
              for (int i = 0; i <= fVisibleLineCount; i++)
                  if (fADDRS[i] == value)
                  {
                      if (fActiveLine != i)
                      {
                          if (i == fVisibleLineCount)
                          {
                              fTopAddress = fADDRS[1];
                              fActiveLine = i - 1;
                          }
                          else
                              fActiveLine = i;
                      }
                      UpdateLines();
                      Refresh();
                      return;
                  }
              TopAddress = value;
          }
      }

      public delegate bool ONCHECKCPU(object Sender, ushort ADDR);
      public event ONCHECKCPU CheckBreakpoint = null;
      public event ONCHECKCPU CheckExecuting = null;
      public delegate void ONGETDATACPU(object Sender, ushort ADDR, int len, out byte[] data);
      public event ONGETDATACPU GetData = null;
      public delegate void ONGETDASMCPU(object Sender, ushort ADDR, out string DASM, out int len);
      public event ONGETDASMCPU GetDasm = null;
      public delegate void ONCLICKCPU(object Sender, ushort Addr);
      public event ONCLICKCPU BreakpointClick = null;
      public event ONCLICKCPU DasmClick = null;

      // private...
      private Timer mouseTimer;

      private ushort fTopAddress;

      private Color fBreakColor;
      private Color fBreakForeColor;
      static private int fGutterWidth = 30;
      public int GetGutterWidth()
      {
          return fGutterWidth;
      }

      private int fLineCount
      {
          get { return fVisibleLineCount + 3; }
      }
      private int fVisibleLineCount;
      private int fLineHeight;
      private int fActiveLine;
      ushort[] fADDRS = null;
      string[] fStrADDRS = null;
      string[] fStrDATAS = null;
      string[] fStrDASMS = null;
      bool[] fBreakpoints = null;
      private Bitmap bitmap = null;
      //comment addresses
      ConcurrentDictionary<ushort/*address*/, string/*comment*/> arrCommentAddrs;
      //notes at address
      ConcurrentDictionary<ushort/*address*/, string/*note*/> arrNoteAddrs;

      public void DrawLines(Graphics g, int x, int y, int wid, int hei)
      {
          if ((Height <= 0) || (Width <= 0)) return;
          if (!Visible) return;
          if ((bitmap == null) || (bitmap.Width != wid) || (bitmap.Height != hei))
              bitmap = new Bitmap(wid, hei);

          using (Graphics gp = Graphics.FromImage(bitmap))
          {
              int wa = (int)gp.MeasureString("DDDD", this.Font).Width;               // "DDDD" width (addr)
              int wd = (int)gp.MeasureString("DDDDDDDDDDDDDDDD", this.Font).Width;   // "DDDDDDDDDDDDDDDD" width (data)
              int wtab = 8;
              int wsp = 8;

              int CurrentY = 0;
              Color ink;
              Color paper;

              gp.FillRectangle(new SolidBrush(BackColor), 0, 0, bitmap.Width, bitmap.Height);

              for (int line = 0; line < fVisibleLineCount; line++)
              {
                  ink = ForeColor;
                  paper = BackColor;

                  bool breakLine = fBreakpoints[line];
                  bool execLine = false;
                  if (CheckExecuting != null)
                      execLine = CheckExecuting(this, fADDRS[line]);

                  Rectangle liner = new Rectangle(fGutterWidth/2, CurrentY, bitmap.Width, fLineHeight);

                  if (breakLine)
                  {
                      ink = fBreakForeColor;
                      paper = fBreakColor;
                      gp.FillRectangle(new SolidBrush(paper), liner);
                  }
                  if (line == fActiveLine)
                  {
                      if (Focused)
                      {
                          ink = Color.White;
                          paper = Color.Navy; //focused line back color
                      }
                      else
                      {
                          ink = Color.Silver;
                          paper = Color.Gray;
                      }
                      gp.FillRectangle(new SolidBrush(paper), liner);
                      /*
                      if ((line == fActiveLine) && Focused) // doted border around selected line...
                      {
                         Point[] lins = new Point[5] 
                         { 
                            new Point(fGutterWidth, CurrentY), new Point(fGutterWidth, CurrentY+fLineHeight-1),
                            new Point(bitmap.Width-1, CurrentY+fLineHeight-1), new Point(bitmap.Width-1, CurrentY),
                            new Point(fGutterWidth, CurrentY) 
                         };
                         gp.DrawLines(new Pen(Color.Yellow), lins);
                      }
                      */
                  }
                  #region Draw gutter icons...
                  if (execLine)      // execarrow icon
                  {
                      int r = 4;    // base
                      int cx = 2 + r;
                      int cy = CurrentY + (fLineHeight / 2);
                      Point[] arr = new Point[7] { new Point(cx,cy-5), new Point(cx, cy-2),
                                    new Point(cx-3, cy-2),
                                    new Point(cx-3, cy+2), new Point(cx, cy+2),
                                    new Point(cx, cy+5),
                                    new Point(cx+5, cy) };
                      gp.FillPolygon(new SolidBrush(Color.Lime), arr);
                      gp.DrawPolygon(new Pen(Color.Black), arr);
                      Point[] shine = new Point[5] { new Point(cx-2, cy+1), new Point(cx-2, cy-1),
                                      new Point(cx+1, cy-1), new Point(cx+1, cy-3),
                                      new Point(cx+4, cy) };
                      gp.DrawLines(new Pen(Color.Yellow), shine);
                  }
                  if (breakLine)  // breakpoint icon
                  {
                      int r = 4;    // half radius
                      Rectangle bpRect;
                      int cx = 2 + r;
                      int cy = CurrentY + (fLineHeight / 2);
                      if (!execLine)
                      {
                          bpRect = new Rectangle(cx - r, cy - r, /*cx +*/ r + r + 1, /*cy +*/ r + r + 1);
                      }
                      else
                      {
                          cx += 16;
                          bpRect = new Rectangle(cx - r, cy - r, /*cx +*/ r + r + 1, /*cy +*/ r + r + 1);
                      }
                      gp.FillEllipse(new SolidBrush(fBreakColor), bpRect);
                      gp.DrawEllipse(new Pen(Color.Black), bpRect);
                  }
                  #endregion


                   if (fStrDATAS[line] == String.Empty) //is comment on address?
                   {
                       gp.DrawString(fStrADDRS[line], this.Font, new SolidBrush(Color.LightGray), fGutterWidth/2, CurrentY);
                       Point[] lins = new Point[2] 
                         { 
                            new Point(fGutterWidth/2, CurrentY+fLineHeight-1), new Point(bitmap.Width-1, CurrentY+fLineHeight-1),
                            /*new Point(bitmap.Width-1, CurrentY+fLineHeight-1), new Point(bitmap.Width-1, CurrentY),
                            new Point(fGutterWidth/2, CurrentY) */
                         };
                       gp.DrawLines(new Pen(Color.Yellow), lins);
                   }
                   else if (   ( this.ForeColor.A == SystemColors.ControlText.A && this.ForeColor.B == SystemColors.ControlText.B && 
                            this.ForeColor.G == SystemColors.ControlText.G && this.ForeColor.R == SystemColors.ControlText.R
                          )
                       || (line == fActiveLine)
                      ) // ToDo: Condition added - in accordance with color we know whether CPU is running or not :-)
                   {
                       gp.DrawString(fStrADDRS[line], this.Font, new SolidBrush(Color.Cyan), fGutterWidth + wsp, CurrentY);
                       gp.DrawString(fStrDATAS[line], this.Font, new SolidBrush(Color.Green), fGutterWidth + wsp + wa + wtab, CurrentY);
                       SyntaxHighligthning(gp, fStrDASMS[line], fStrDATAS[line], this.Font, fGutterWidth + wsp + wa + wtab + wd + wtab, CurrentY, fADDRS[line]);
                   }
                   else
                   {
                      gp.DrawString(fStrADDRS[line], this.Font, new SolidBrush(ink), fGutterWidth + wsp, CurrentY); //address
                      gp.DrawString(fStrDATAS[line], this.Font, new SolidBrush(ink), fGutterWidth + wsp + wa + wtab, CurrentY); //opcodes
                      gp.DrawString(fStrDASMS[line], this.Font, new SolidBrush(ink), fGutterWidth + wsp + wa + wtab + wd + wtab, CurrentY); //disassembly and tact count
                   }

                   CurrentY += fLineHeight;
              }
          }
          g.DrawImageUnscaled(bitmap, x, y);// DrawImage(bitmap, x, y);
      }

      private bool IsJumpInstructionType(string i_cpuInstructionText)
      {
          return i_cpuInstructionText.StartsWith("JP") || i_cpuInstructionText.StartsWith("JR") || i_cpuInstructionText.StartsWith("CALL") ||
                 i_cpuInstructionText.StartsWith("RET") || i_cpuInstructionText.StartsWith("RST");

      }
      private void SyntaxHighligthning( Graphics gp, string cpuInstruction, string instructionOpcode, Font font, int startX, int startY, ushort noteAddress)
      {
          string     originalString = cpuInstruction;
          string     cpuParsed      = String.Empty;

          StringFormat f = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };

          SolidBrush solidChar = new SolidBrush(Color.DarkSalmon);
          SolidBrush solidNum  = new SolidBrush(Color.DarkSeaGreen);
          SolidBrush solidRegistry = new SolidBrush(Color.LightBlue);
          SolidBrush solidJumpOrCallInstruction = new SolidBrush(Color.DarkViolet);

          int         actX = startX /*- 30*/;
          bool        parsingLetters = true; // actual char parsing; default to true because cpu instruction always starts with the character
          char        lastChar = ' ';

          //change color if jump/call instruction type
          if (IsJumpInstructionType(cpuInstruction))
              solidChar = solidJumpOrCallInstruction;

          //OpCode Tact
          bool        parsingOpcodeTact = false;
          string      opcodeTactParsed = String.Empty;

          for (byte counter = 0; counter < originalString.Length; counter++)
          {
              if (counter > 0)
                  lastChar = originalString[counter - 1]; // remember last char

              char curChar = originalString[counter];
              if (curChar == ';' || parsingOpcodeTact)
              {
                  opcodeTactParsed += curChar;
                  parsingOpcodeTact = true;
                  continue;
              }

              if (parsingLetters)
              {
                  if (Char.IsLetter(originalString[counter]) || curChar == ' ')
                  {
                      cpuParsed += originalString[counter];
                      continue;
                  }
                  else
                  {
                      if (Char.IsDigit(curChar) || curChar == '%' || curChar == '#')
                      {
                          // swap letters -> digits
                          gp.DrawString(cpuParsed, font, solidChar, actX, startY); // normal text(string), startY - always the same, because the same line
                          actX += ((int)gp.MeasureString(cpuParsed, font, new PointF(0,0), f).Width);

                          if (lastChar == ',')
                              cpuParsed = ' ' + curChar.ToString(); // add blank space when last character was comma(',')
                          else
                              cpuParsed = curChar.ToString();

                          parsingLetters = false;
                      }
                      else
                      {
                          // here it can be ",", "(", ")"...
                          cpuParsed += curChar.ToString();
                          continue;
                      }
                  }
              }
              else
              {
                  if ( (curChar != ',' && curChar != '(' && curChar != ')') || curChar == ' ')
                  {
                      cpuParsed += curChar.ToString();
                      continue;
                  }
                  else
                  {
                      // swap digits -> letters
                      gp.DrawString(cpuParsed, font, solidNum, actX, startY); // normal text(string), startY - always the same, because the same line
                      actX += ((int)gp.MeasureString(cpuParsed, font, new PointF(0,0), f).Width);

                      if (lastChar == ',')
                          cpuParsed = ' ' + curChar.ToString(); // add blank space when last character was comma(',')
                      else
                          cpuParsed = curChar.ToString();

                      parsingLetters = true;
                  }
              }
          }

          if (parsingLetters)
              gp.DrawString(cpuParsed, font, solidChar, actX, startY);
          else
              gp.DrawString(cpuParsed, font, solidNum, actX, startY); // normal text(string), startY - always the same, because the same line

          //Code notes
          if (IsCodeNoteAtAddress(noteAddress))
          {
              Font fontNote = new Font(font.Name, font.Size - 2, FontStyle.Italic);
              int noteStartX = ((int)gp.MeasureString(new string(' ', cpuParsed.TrimEnd().Length), font, new PointF(0, 0), f).Width);
              gp.DrawString(arrNoteAddrs[noteAddress], fontNote, new SolidBrush(Color.Gray), actX + noteStartX + 5, startY + 2);
          }

          //OpCode processor Tact/s
          gp.DrawString(opcodeTactParsed, font, new SolidBrush(Color.Gray), this.Width - 50, startY);

          return;
      }
      public ushort[] GetNumberFromCpuInstruction_ActiveLine()
      {
          if (fStrDASMS[fActiveLine].Contains("#"))
          {
              ushort[] addressOut = new ushort[1];
              var matches = Regex.Matches(fStrDASMS[fActiveLine], @"#([0-9A-F]*)");
              string matchedNum = matches[0].ToString().Substring(1, matches[0].Length - 1);

              addressOut[0] = ushort.Parse(matchedNum, System.Globalization.NumberStyles.HexNumber);
              return addressOut;
          }

          return null;
      }

      //disassembly history
      private List<ushort> _arrAddrHistory; //ToDo: change to Stack() class, see: https://msdn.microsoft.com/en-us/library/system.collections.stack%28v=vs.110%29.aspx
      private int _dAddHistoryCurrentIndex = 0;

      public void AddAddrToDisassemblyHistory(ushort i_addrToAdd)
      {
          if (_arrAddrHistory == null)
              _arrAddrHistory = new List<ushort>();
          _arrAddrHistory.Add(i_addrToAdd);
          _dAddHistoryCurrentIndex = _arrAddrHistory.Count-1;
      }
      public ushort GetForwardAddrFromHistory()
      {
          if (_arrAddrHistory == null)
              return (ushort)this.TopAddress;
          if (_arrAddrHistory.Count-1 > _dAddHistoryCurrentIndex)
            _dAddHistoryCurrentIndex++;

          return _arrAddrHistory.ElementAt(_dAddHistoryCurrentIndex);
      }
      public ushort GetBackAddrFromHistory()
      {
          if (_arrAddrHistory == null)
              return (ushort)this.TopAddress;
          if (_dAddHistoryCurrentIndex > 0)
              _dAddHistoryCurrentIndex--;

          return _arrAddrHistory.ElementAt(_dAddHistoryCurrentIndex);
      }

      //code comments
      public void InsertCodeComment(ushort i_addr, string i_comment)
      {
          if (arrCommentAddrs == null)
              arrCommentAddrs = new ConcurrentDictionary<ushort, string>();

          if (IsCodeCommentAtAddress(i_addr))
              arrCommentAddrs[i_addr] = i_comment;
          else
              arrCommentAddrs.TryAdd(i_addr, i_comment);
          UpdateLines();
          Refresh();
      }
      public string GetCodeCommentAtAddress(ushort i_addr)
      {
          if (!IsCodeCommentAtAddress(i_addr))
              return String.Empty;

          return arrCommentAddrs[i_addr];
      }
      public bool IsCodeCommentAtAddress(ushort i_addrToCheck)
      {
          if (arrCommentAddrs == null || arrCommentAddrs.Count == 0)
              return false;

          return arrCommentAddrs.Keys.Contains(i_addrToCheck);
      }
      public bool IsCodeCommentAtAddress(ushort i_addrToCheck, ref string o_commentAtAddress)
      {
          if (arrCommentAddrs == null || arrCommentAddrs.Count == 0)
              return false;

          bool hasCommentAtAddress = IsCodeCommentAtAddress(i_addrToCheck);
          if (hasCommentAtAddress)
              o_commentAtAddress = arrCommentAddrs[i_addrToCheck];
          return hasCommentAtAddress;
      }
      public void ClearCodeComment(ushort i_addr)
      {
          if (arrCommentAddrs == null)
              return;

          if (arrCommentAddrs.Keys.Contains(i_addr))
          {
              string valueOut;
              arrCommentAddrs.TryRemove(i_addr, out valueOut);
              UpdateLines();
              Refresh();
          }
      }
      public void ClearCodeComments()
      {
          if (arrCommentAddrs == null)
              return;

          arrCommentAddrs.Clear();
          arrCommentAddrs = null;
          UpdateLines();
          Refresh();
      }
      public ConcurrentDictionary<ushort, string> GetCodeComments()
      {
          return arrCommentAddrs;
      }
      public void SetCodeComments(ConcurrentDictionary<ushort, string> i_arrCodeComments)
      {
          arrCommentAddrs = i_arrCodeComments;
          UpdateLines();
      }

      //notes at address
      public void InsertCodeNote(ushort i_addr, string i_note)
      {
          if (arrNoteAddrs == null)
              arrNoteAddrs = new ConcurrentDictionary<ushort, string>();

          if (IsCodeNoteAtAddress(i_addr))
              arrNoteAddrs[i_addr] = i_note;
          else
              arrNoteAddrs.TryAdd(i_addr, i_note);
          UpdateLines();
          Refresh();
      }
      public bool IsCodeNoteAtAddress(ushort i_addrToCheck)
      {
          if (arrNoteAddrs == null || arrNoteAddrs.Count == 0)
              return false;

          return arrNoteAddrs.Keys.Contains(i_addrToCheck);
      }
      public bool IsCodeNoteAtAddress(ushort i_addrToCheck, ref string o_commentAtAddress)
      {
          if (arrNoteAddrs == null || arrNoteAddrs.Count == 0)
              return false;

          bool hasCommentAtAddress = IsCodeNoteAtAddress(i_addrToCheck);
          if (hasCommentAtAddress)
              o_commentAtAddress = arrNoteAddrs[i_addrToCheck];
          return hasCommentAtAddress;
      }
      public ConcurrentDictionary<ushort, string> GetCodeNotes()
      {
          return arrNoteAddrs;
      }
      public void SetCodeNotes(ConcurrentDictionary<ushort, string> i_arrCodeNotes)
      {
          arrNoteAddrs = i_arrCodeNotes;
          UpdateLines();
      }
      public void ClearCodeNote(ushort i_addr)
      {
          if (arrNoteAddrs == null)
              return;

          if (arrNoteAddrs.Keys.Contains(i_addr))
          {
              string valueOut;
              arrNoteAddrs.TryRemove(i_addr, out valueOut);
              UpdateLines();
              Refresh();
          }
      }
      public void ClearCodeNotes()
      {
          if (arrNoteAddrs == null)
              return;

          arrNoteAddrs.Clear();
          arrNoteAddrs = null;
          UpdateLines();
          Refresh();
      }

      public void UpdateLines()
      {
          fADDRS = new ushort[fLineCount];
          fStrADDRS = new string[fLineCount];
          fStrDATAS = new string[fLineCount];
          fStrDASMS = new string[fLineCount];
          fBreakpoints = new bool[fLineCount];

          ushort CurADDR = fTopAddress;
          for (int i = 0; i < fLineCount; i++)
          {
              fADDRS[i] = CurADDR;

              if (IsCodeCommentAtAddress(CurADDR)) //display comment in disassembly panel
              {
                  fStrDASMS[i] = String.Empty;
                  fStrDATAS[i] = String.Empty;
                  fStrADDRS[i++] = GetCodeCommentAtAddress(CurADDR);
                  if (i >= fLineCount)
                      break;
              }
              fStrADDRS[i] = CurADDR.ToString("X4");
              fADDRS[i] = CurADDR;

              string dasm;
              int len;
              byte[] data;

              if (GetDasm != null)
                  GetDasm(this, CurADDR, out dasm, out len);
              else
              {
                  dasm = "???";
                  len = 1;
              }
              if (GetData != null)
                  GetData(this, CurADDR, len, out data);
              else
                  data = new byte[] { 0x00 };

              fStrDASMS[i] = dasm;
              string sdata = "";
              int maxdata = data.Length;
              if (maxdata > 7) maxdata = 7;
              for (int j = 0; j < maxdata; j++)
                  sdata += data[j].ToString("X2");
              if (maxdata < data.Length)
                  sdata += "..";
              fStrDATAS[i] = sdata;
              if (CheckBreakpoint != null)
              {
                  if (CheckBreakpoint(this, CurADDR))
                      fBreakpoints[i] = true;
              }
              else
                  fBreakpoints[i] = false;
              CurADDR += (ushort)len;
          }
      }

      private void ControlUp()
      {
          fActiveLine--;
          if (fActiveLine < 0)
          {
              fActiveLine++;
              fTopAddress = (ushort)(fADDRS[0] - 1);
              UpdateLines();
          }
      }
      private void ControlDown()
      {
          fActiveLine++;
          if (fActiveLine >= fVisibleLineCount)
          {
              if (IsCodeCommentAtAddress(fTopAddress))
                fTopAddress = fADDRS[2];
              else
                fTopAddress = fADDRS[1];
              fActiveLine--;
              UpdateLines();
          }
      }
      private void ControlPageUp()
      {
          for (int i = 0; i < (fVisibleLineCount - 1); i++)
          {
              fTopAddress--;
              UpdateLines();
          }
      }
      private void ControlPageDown()
      {
          if (fVisibleLineCount > 0)
          {
              fTopAddress = fADDRS[fVisibleLineCount - 1];
              UpdateLines();
          }
          fTopAddress = fADDRS[1];
          UpdateLines();
      }

      protected override void OnKeyDown(KeyEventArgs e)
      {
          switch (e.KeyCode)
          {
              case Keys.Down:
                  ControlDown();
                  Invalidate();
                  break;
              case Keys.Up:
                  ControlUp();
                  Invalidate();
                  break;
              case Keys.PageDown:
                  ControlPageDown();
                  Invalidate();
                  break;
              case Keys.PageUp:
                  ControlPageUp();
                  Invalidate();
                  break;
              case Keys.Enter:
                  if (DasmClick != null)
                      DasmClick(this, fADDRS[fActiveLine]);
                  UpdateLines();
                  Refresh();
                  break;
          }
          base.OnKeyDown(e);
      }
      protected override void OnMouseDown(MouseEventArgs e)
      {
          if ((e.Button & MouseButtons.Left) != 0)
          {
              int nl = (e.Y - 1) / fLineHeight;
              if (nl < fVisibleLineCount)
                  if (nl != fActiveLine)
                  {
                      fActiveLine = nl;
                      Invalidate();
                  }
              mouseTimer.Enabled = true;
          }
          else if (e.Button == MouseButtons.XButton1)
          {
              //back
              this.TopAddress = GetBackAddrFromHistory();
          }
          else if (e.Button == MouseButtons.XButton2)
          {
              //forward
              this.TopAddress = GetForwardAddrFromHistory();
          }
          base.OnMouseDown(e);
      }
      protected override void OnMouseMove(MouseEventArgs e)
      {
          if ((e.Button & MouseButtons.Left) != 0)
          {
              int nl = (e.Y - 1) / fLineHeight;
              if (nl < 0) return;
              if (nl < fVisibleLineCount)
                  if (nl != fActiveLine)
                  {
                      fActiveLine = nl;
                      Invalidate();
                  }
          }
          base.OnMouseMove(e);
      }
      protected override void OnMouseWheel(MouseEventArgs e)
      {
          int delta = -e.Delta / 30; //Adlers: 30 best for me
          if (delta < 0)
              for (int i = 0; i < -delta; i++)
                  ControlUp();
          else
              for (int i = 0; i < delta; i++)
                  ControlDown();
          Invalidate();

          base.OnMouseWheel(e);
      }
      protected override void OnMouseCaptureChanged(EventArgs e)
      {
          mouseTimer.Enabled = false;
          base.OnMouseCaptureChanged(e);
      }
      private void OnMouseTimer(object sender, EventArgs e)
      {
          Point mE = PointToClient(MousePosition);
          int nl = (mE.Y - 1);
          if (nl < 0)
          {
              ControlUp();
              Invalidate();
          }
          if (nl >= (fVisibleLineCount * fLineHeight))
          {
              ControlDown();
              Invalidate();
          }
      }

      protected override void OnPaintBackground(PaintEventArgs e)
      {
          //         base.OnPaintBackground(e);
          /*
                   Pen penBlack = new Pen(Color.Black);
                   Pen penSilver = new Pen(Color.Ivory);
                   e.Graphics.DrawLine(penBlack, 0, 0, Width - 1, 0);
                   e.Graphics.DrawLine(penBlack, 0, 0, 0, Height - 1);

                   e.Graphics.DrawLine(penSilver, 0, Height - 1, Width - 1, Height - 1);
                   e.Graphics.DrawLine(penSilver, Width - 1, Height - 1, Width - 1, 0);
           */
      }
      protected override void OnPaint(PaintEventArgs e)
      {
          //         base.OnPaint(e);
          //         fAntispacing = this.Font.GetHeight(e.Graphics);
          //         int lh = (int)Math.Ceiling((double)this.Font.GetHeight(e.Graphics)*((double)this.Font.FontFamily.GetEmHeight(this.Font.Style) / (double)this.Font.FontFamily.GetLineSpacing(this.Font.Style)));
          //         fAntispacing = (fAntispacing - (float)lh)/2f;
          int lh = (int)e.Graphics.MeasureString("3D,", this.Font).Height;
          int lc = ((Height - 2) / lh);
          if (lc < 0) lc = 0;
          if ((fVisibleLineCount != lc) || (fLineHeight != lh))
          {
              fLineHeight = lh;
              fVisibleLineCount = lc;
              UpdateLines();
          }
          // chk...
          if ((fActiveLine >= fVisibleLineCount) && (fVisibleLineCount > 0))
          {
              fActiveLine = fVisibleLineCount - 1;
              Invalidate();
          }
          else if ((fActiveLine < 0) && (fVisibleLineCount > 0))
          {
              fActiveLine = 0;
              Invalidate();
          }
          DrawLines(e.Graphics, 0, 0, ClientRectangle.Width, ClientRectangle.Height);// Width - 2, Height - 2);
      }

      protected override void OnGotFocus(EventArgs e)
      {
          base.OnGotFocus(e);
          Invalidate();
      }
      protected override void OnLostFocus(EventArgs e)
      {
          base.OnLostFocus(e);
          Invalidate();
      }
      protected override bool IsInputKey(Keys keyData)
      {
          Keys keys1 = keyData & Keys.KeyCode;
          switch (keys1)
          {
              case Keys.Left:
              case Keys.Up:
              case Keys.Right:
              case Keys.Down:
                  return true;
          }
          return base.IsInputKey(keyData);
      }

      protected override void OnMouseDoubleClick(MouseEventArgs e)
      {
          if ((e.Button & MouseButtons.Left) != 0)
          {
              int nl = e.Y / fLineHeight;
              if (nl < fVisibleLineCount)
              {
                  if (e.X <= fGutterWidth)
                  {
                      if (BreakpointClick != null)
                          BreakpointClick(this, fADDRS[nl]);
                      UpdateLines();
                      Refresh();
                  }
                  else
                  {
                      if (DasmClick != null)
                          DasmClick(this, fADDRS[nl]);
                      UpdateLines();
                      Refresh();
                  }
              }
          }
          base.OnMouseDoubleClick(e);
      }
   }
   public class DataPanel : Control
   {
      public DataPanel()
      {
         TabStop = true;
         this.Font = new Font("Courier", 13,System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
         Size = new System.Drawing.Size(424, 99);
         ControlStyles styles = ControlStyles.Selectable |
                                ControlStyles.UserPaint |
                                ControlStyles.ResizeRedraw |
                                ControlStyles.StandardClick |           // csClickEvents
                                ControlStyles.UserMouse |               // csCaptureMouse
                                ControlStyles.ContainerControl |        // csAcceptsControls?
                                ControlStyles.StandardDoubleClick |     // csDoubleClicks
                                0;
         base.SetStyle(styles, true);

         mouseTimer = new Timer();
         mouseTimer.Enabled = false;
         mouseTimer.Interval = 50;
         mouseTimer.Tick += OnMouseTimer;

         fLineHeight = 1;
         fVisibleLineCount = 0;
         fTopAddress = 0;
         fActiveLine = 0;
         fActiveColumn = 0;
         fColCount = 8;
         UpdateLines();
      }

      public ushort TopAddress
      {
         get { return fTopAddress; }
         set 
         {
            fTopAddress = value;
            fActiveLine = 0;
            UpdateLines();
            Invalidate();
         }
      }
      public int ColCount
      {
         get { return fColCount; }
         set
         {
            fColCount = value;
            UpdateLines();
            Invalidate();
         }
      }
       public ushort ActiveAddress
      {
          get { 
              
              return Convert.ToUInt16(fTopAddress + (fActiveLine*fColCount)+fActiveColumn); 
          }
      }

      public delegate void ONGETDATACPU(object Sender, ushort ADDR, int len, out byte[] data);
      public event ONGETDATACPU GetData = null;
      public delegate void ONCLICKCPU(object Sender, ushort Addr);
      public event ONCLICKCPU DataClick = null;


      // private...
      private Timer mouseTimer;
      private ushort fTopAddress;

      static private int fGutterWidth = 30;
      private int fColCount;
      private int fLineCount
      {
         get { return fVisibleLineCount + 3; }
      }
      private int fVisibleLineCount;
      private int fLineHeight;
      private int fActiveLine;
      private int fActiveColumn;
      ushort[] fADDRS = null;
      byte[][] fBytesDATAS = null;
      private Bitmap bitmap = null;
      private int wa = 0;
      private int wd = 0;
      private int wtab = 0;
      private int wsp = 0;
      private int wsymb = 0;


      public void DrawLines(Graphics g, int x, int y, int wid, int hei)
      {
         if ((Height <= 0) || (Width <= 0)) return;
         if (!Visible) return;
         if ((bitmap == null) || (bitmap.Width != wid) || (bitmap.Height != hei))
            bitmap = new Bitmap(wid, hei);

         using (Graphics gp = Graphics.FromImage(bitmap))
         {
            int wdsp = 2;
            wa = (int)gp.MeasureString("DDDD", this.Font).Width;             // "DDDD" width (addr)
            wd = (int)gp.MeasureString("DD", this.Font).Width+wdsp*2;        // "DD" width (data)
            wsymb = (int)gp.MeasureString("D", this.Font).Width;
            wtab = 8;
            wsp = 8;

            int CurrentY = 0;
            Color ink;
            Color paper;

            gp.FillRectangle(new SolidBrush(BackColor), 0, 0, bitmap.Width, bitmap.Height);

            
            for (int line = 0; line < fVisibleLineCount; line++)
            {
               ink = ForeColor;
               paper = BackColor;

               gp.DrawString(fADDRS[line].ToString("X4"), this.Font, new SolidBrush(ink), fGutterWidth+wsp, CurrentY);

               for(int col=0; col < fColCount; col++)
               {
                  ink = ForeColor;
                  paper = BackColor;
                  if ((line == fActiveLine) && (col == fActiveColumn))
                  {
                     if (Focused)
                     {
                        ink = Color.White;
                        paper = Color.Navy;
                     }
                     else
                     {
                        ink = Color.Silver;
                        paper = Color.Gray;
                     }
                     gp.FillRectangle(new SolidBrush(paper), new Rectangle(fGutterWidth + wsp + wa + wtab + (col * wd), CurrentY, wd, fLineHeight));
                     gp.FillRectangle(new SolidBrush(Color.Gray), new Rectangle(fGutterWidth + wsp + wa + wtab + (fColCount * wd) + wtab + (col * wsymb), CurrentY, wsymb, fLineHeight));
                     /*
                     if (Focused) // doted border around selected line...
                     {
                        Point[] lins = new Point[5] 
                        { 
                           new Point(fGutterWidth + wsp + wa + wtab + (col * wd), CurrentY), 
                           new Point(fGutterWidth + wsp + wa + wtab + (col * wd), CurrentY+fLineHeight-1),
                           new Point(fGutterWidth + wsp + wa + wtab + (col * wd)+wd-1, CurrentY+fLineHeight-1), 
                           new Point(fGutterWidth + wsp + wa + wtab + (col * wd)+wd-1, CurrentY),
                           new Point(fGutterWidth + wsp + wa + wtab + (col * wd), CurrentY) 
                        };
                        gp.DrawLines(new Pen(Color.Yellow), lins);
                     }
                     */
                  }
                  gp.DrawString(fBytesDATAS[line][col].ToString("X2"), this.Font, new SolidBrush(ink), fGutterWidth + wsp + wa + wtab + (col * wd) + wdsp, CurrentY);
                  string sch = new String(zxencode[fBytesDATAS[line][col]], 1);
                  gp.DrawString(sch, this.Font, new SolidBrush(ink), fGutterWidth + wsp + wa + wtab + (fColCount * wd) + wtab + (col * wsymb), CurrentY);
               }
               CurrentY += fLineHeight;
            }
         }
         g.DrawImageUnscaled(bitmap, x, y);// DrawImage(bitmap, x, y);
      }
      public void UpdateLines()
      {
         fADDRS = new ushort[fLineCount];
         fBytesDATAS = new byte[fLineCount][];

         ushort CurADDR = fTopAddress;
         for (int i = 0; i < fLineCount; i++)
         {
            fADDRS[i] = CurADDR;

            if (GetData != null)
               GetData(this, CurADDR, fColCount, out fBytesDATAS[i]);
            else
            {
               fBytesDATAS[i] = new byte[fColCount];
               for (int j = 0; j < fColCount; j++)
                  fBytesDATAS[i][j] = (byte)((fTopAddress+i*fColCount+j)&0xFF);
            }
            CurADDR += (ushort)fColCount;
         }
      }

      private void ControlUp()
      {
         fActiveLine--;
         if (fActiveLine < 0)
         {
            fActiveLine++;
            fTopAddress -= (ushort)fColCount;
            UpdateLines();
         }
      }
      private void ControlDown()
      {
         fActiveLine++;
         if (fActiveLine >= fVisibleLineCount)
         {
            fTopAddress += (ushort)fColCount;
            fActiveLine--;
            UpdateLines();
         }
      }
      private void ControlLeft()
      {
         fActiveColumn--;
         if (fActiveColumn < 0)
         {
            fActiveColumn = fColCount - 1;
            ControlUp();
         }
         else
            UpdateLines();
      }
      private void ControlRight()
      {
         fActiveColumn++;
         if (fActiveColumn >= fColCount)
         {
            fActiveColumn = 0;
            ControlDown();
         }
         else
            UpdateLines();
      }
      private void ControlPageUp()
      {
         fTopAddress -= (ushort)(fColCount*fVisibleLineCount);
         UpdateLines();
      }
      private void ControlPageDown()
      {
         fTopAddress += (ushort)(fColCount * fVisibleLineCount);
         UpdateLines();
      }


      protected override void OnMouseDown(MouseEventArgs e)
      {
         if ((e.Button & MouseButtons.Left) != 0)
         {
            int nl = (e.Y - 1) / fLineHeight;
            if ((nl < fVisibleLineCount) && (nl >= 0))
            {
               if (nl != fActiveLine)
               {
                  fActiveLine = nl;
                  Invalidate();
               }
            }
            int nc;
            if (wd >= 0)
            {
               nc = ((e.X - 1) - (fGutterWidth + wsp + wa + wtab));
               if (nc >= 0) nc /= wd;
               else nc = -1;
            }
            else
               nc = 0;
            if ((nc < fColCount) && (nc >= 0))
            {
               if (nc != fActiveColumn)
               {
                  fActiveColumn = nc;
                  Invalidate();
               }
            }
            mouseTimer.Enabled = true;
         }
         base.OnMouseDown(e);
      }
      protected override void OnMouseMove(MouseEventArgs e)
      {
         if ((e.Button & MouseButtons.Left) != 0)
         {
            int nl = (e.Y - 1) / fLineHeight;
            if ((nl < fVisibleLineCount) && (nl >= 0))
            {
               if (nl != fActiveLine)
               {
                  fActiveLine = nl;
                  Invalidate();
               }
            }
            int nc;
            if (wd >= 0)
            {
               nc = ((e.X - 1) - (fGutterWidth + wsp + wa + wtab));
               if (nc >= 0) nc /= wd;
               else nc = -1;
            }
            else
               nc = 0;
            if ((nc < fColCount) && (nc >= 0))
            {
               if (nc != fActiveColumn)
               {
                  fActiveColumn = nc;
                  Invalidate();
               }
            }
         }
         base.OnMouseMove(e);
      }
      protected override void OnMouseWheel(MouseEventArgs e)
      {
          int delta = -e.Delta / 30; //Adlers: 30 best for me
          if (delta < 0)
             for (int i = 0; i < -delta; i++)
                ControlUp();
          else
             for (int i = 0; i < delta; i++)
                ControlDown();
          Invalidate();

          base.OnMouseWheel(e);
      }
      protected override void OnMouseCaptureChanged(EventArgs e)
      {
         mouseTimer.Enabled = false;
         base.OnMouseCaptureChanged(e);
      }
      private void OnMouseTimer(object sender, EventArgs e)
      {
         Point mE = PointToClient(MousePosition);
         int nl = (mE.Y - 1);
         if (nl < 0)
         {
            ControlUp();
            Invalidate();
         }
         if (nl >= (fVisibleLineCount*fLineHeight))
         {
            ControlDown();
            Invalidate();
         }

         int nc;
         if (wd >= 0)
         {
            nc = ((mE.X - 1) - (fGutterWidth + wsp + wa + wtab));
            if (nc >= 0) nc /= wd;
            else nc = -1;
         }
         else
            nc = 0;
         if ((nc < fColCount) && (nc >= 0))
         {
            if (nc != fActiveColumn)
            {
               fActiveColumn = nc;
               Invalidate();
            }
         }
      }
      protected override void OnMouseDoubleClick(MouseEventArgs e)
      {
         if ((e.Button & MouseButtons.Left) != 0)
         {
            int nl = e.Y / fLineHeight;
            int nc;
            if (wd >= 0)
               nc = ((e.X - 1) - (fGutterWidth + wsp + wa + wtab)) / wd;
            else
               nc = 0;
            if ((nl < fVisibleLineCount)&&(nl>=0))
            {
               if ((nc < fColCount)&&(nc>=0))
               {
                  if (DataClick != null)
                     DataClick(this, (ushort)(fADDRS[nl]+nc));
                  UpdateLines();
                  Refresh();
               }
            }
         }
         base.OnMouseDoubleClick(e);
      }


      protected override void OnGotFocus(EventArgs e)
      {
         base.OnGotFocus(e);
         Invalidate();
      }
      protected override void OnLostFocus(EventArgs e)
      {
         base.OnLostFocus(e);
         Invalidate();
      }
      protected override bool IsInputKey(Keys keyData)
      {
         Keys keys1 = keyData & Keys.KeyCode;
         switch (keys1)
         {
               case Keys.Left:
               case Keys.Up:
               case Keys.Right:
               case Keys.Down:
                  return true;
         }
         return base.IsInputKey(keyData);
      }
      protected override void OnKeyDown(KeyEventArgs e)
      {
         switch (e.KeyCode)
         {
            case Keys.Down:
               ControlDown();
               Invalidate();
               break;
            case Keys.Up:
               ControlUp();
               Invalidate();
               break;
            case Keys.Left:
               ControlLeft();
               Invalidate();
               break;
            case Keys.Right:
               ControlRight();
               Invalidate();
               break;
            case Keys.PageDown:
               ControlPageDown();
               Invalidate();
               break;
            case Keys.PageUp:
               ControlPageUp();
               Invalidate();
               break;
            case Keys.Enter:
               if (fVisibleLineCount > 0)
               {
                  if (DataClick != null)
                     DataClick(this, (ushort)(fADDRS[fActiveLine] + fActiveColumn));
               }
               UpdateLines();
               Refresh();
               break;
         }
         base.OnKeyDown(e);
      }
      protected override void OnPaintBackground(PaintEventArgs e)
      {
      }
      protected override void OnPaint(PaintEventArgs e)
      {
         int lh = (int)e.Graphics.MeasureString("3D,", this.Font).Height;
         int lc = ((Height - 2) / lh);
         if (lc < 0) lc = 0;
         if ((fVisibleLineCount != lc) || (fLineHeight != lh))
         {
            fLineHeight = lh;
            fVisibleLineCount = lc;
            UpdateLines();
         }
// chk...
         if ((fActiveLine >= fVisibleLineCount) && (fVisibleLineCount > 0))
         {
            fActiveLine = fVisibleLineCount - 1;
//            Invalidate();
         }
         else if ((fActiveLine < 0) && (fVisibleLineCount > 0))
         {
            fActiveLine = 0;
//            Invalidate();
         }

         DrawLines(e.Graphics, 0, 0, ClientRectangle.Width, ClientRectangle.Height);// Width - 2, Height - 2);
      }

      static char[] zxencode = new char[256]
      {
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // 00..0F
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // 10..1F
         ' ','!','"','#','$','%','&','\'','(',')','*','+',',','-','.','/', // 20..2F
         '0','1','2','3','4','5','6','7','8','9',':',';','<','=','>','?',  // 30..3F
         '@','A','B','C','D','E','F','G','H','I','J','K','L','M','N','O',  // 40..4F
         'P','Q','R','S','T','U','V','W','X','Y','Z','[','\\',']','↑','_', // 50..5F
         '₤','a','b','c','d','e','f','g','h','i','j','k','l','m','n','o',  // 60..6F
         'p','q','r','s','t','u','v','w','x','y','z','{','|','}','~','©',  // 70..7F

         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // 80..8F
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // 90..9F
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // A0..AF
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // B0..BF
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // C0..CF
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // D0..DF
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // E0..EF
         '.','.','.','.','.','.','.','.','.','.','.','.','.','.','.','.',  // F0..FF
      };
   }
}