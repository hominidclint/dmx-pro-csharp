﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text.RegularExpressions;
using basic_light_board.Properties;
using System.IO;

namespace basic_light_board
{
    public delegate void Action<T1, T2>(T1 arg1, T2 arg2);
    /// <summary>
    /// this form provides a basic 24 channel 1 to 1 patched XY cross fader.
    /// it allows for timed fades using a go button or manual time control using a crossfader.
    /// this currently does not interface with the Enttec USB pro because i havent gotten the thing yet
    /// if all goes well it should work just fine.
    /// </summary>
    public partial class Form1 : Form
    {
        public const int universeSize=512;
        public const int ShowSaveVersion = 200;

        /// <summary>
        /// LiveLevels contains one entry for every channel
        /// LiveLevels[0] contains the value for channel 1
        /// </summary>
        byte[] LiveLevels = new byte[universeSize];

        public CueList CList
        {
            get;
            set;
        }


        CueList mCList;
        LightCue blindCue;
        output m_outForm;
        Stopwatch m_timer;

        VComWrapper com;

        int iterations;
        int change;

        #region Form Events & constructor
        public Form1()
        {
            SliderGroup.Labels = Settings.Default.Labels.Split(',');
            SliderGroup.LabelChanged += new EventHandler<LabelChangedArgs>(SliderGroup_LabelChanged);
            InitializeComponent();

            mCList = new CueList();
            mCList.nextCueChanged += new EventHandler(mCList_nextCueChanged);
            mCList.currentCueChanged += new EventHandler(mCList_currentCueChanged);
            mCList.NextCueNumber = 0;
            //mCList.cueChanged += new EventHandler(mCList_cueChanged);


            loadCueIntoBlind(0);
            blindCue.channelLevelsChanged += new EventHandler(blindCue_channelLevelsChanged);

            

            com = new VComWrapper();
            com.SerialNumberReceived += new EventHandler<SerialNumberArgs>(com_SerialNumberReceived);
            com.WidgetParametersReceived += new EventHandler<WidgetParameterArgs>(com_WidgetParametersReceived);
        }

        void blindCue_channelLevelsChanged(object sender, EventArgs e)
        {
            if (blindCue.cueNumber == mCList.CurrentCueNumber)
            {
                outputLightMix(LiveLevels, SliderGroup.Patchlist, SliderGroup.Level,
              sliderGroupLive.ChannelValues,
              mCList.CurrentCue.channelLevels, mCList.NextCue.channelLevels,
              crossfaders1.CurrentSceneValue, crossfaders1.NextSceneValue);                                                                                                               
            }
        }

        void mCList_currentCueChanged(object sender, EventArgs e)
        {
            updateCueLabels();
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            com.detatchPro();
            // save the labels
            //String.Join(",", intArray.Select(i => i.ToString()).ToArray());
            mCList.saveToFile(Settings.Default.cueFile);
            Settings.Default.Save();
        }
        private void Form1_Load(object sender, EventArgs e)
        {

            m_outForm = new output(96);
            m_outForm.Show();

            comboBox1.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames());
            comboBox1.SelectedIndex = 0;
            this.Text = "Not Connected";

            if (File.Exists(Settings.Default.cueFile))
            {
                mCList.loadFromFile(Settings.Default.cueFile);
            }
        }
        #endregion

        #region Form update Methods
        private void updateTextBox()
        {
            int numCharsWide = (int)(textBox1.Width / textBox1.Font.Size );
            int numItemsPerLine = numCharsWide / 11;
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < universeSize; i++)
            {
                str.AppendFormat("Ch{0,3}:{1,4} ", i + 1, LiveLevels[i]);
                if (i != 0 && ((i+1) % numItemsPerLine) == 0) str.AppendLine();


            }
            textBox1.Text = str.ToString();
        }
        private void updateWidget()
        {
            if (com == null) return;
            if (com.IsOpen == false) return;
            if (com.m_port.BytesToWrite > 0) return;

            com.sendDMXPacketRequest(LiveLevels);
        }
        private void updateOutForm()
        {
            if (m_outForm == null) return;
            for (int i = 0; i < m_outForm.m_Bars.Count; i++)
            {
                m_outForm.m_Bars[i].Value = LiveLevels[i];
            }
        }
        void updateFader(byte c,byte n)
        {
            crossfaders1.CurrentSceneValue = c;
            crossfaders1.NextSceneValue = n;
        }

        #endregion

        #region handle CueList Events
        void mCList_cueChanged(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }
        void mCList_nextCueChanged(object sender, EventArgs e)
        {
            sliderGroupNext.ChannelValues = mCList.NextCue.channelLevels;
            updateCueLabels();
        }
        #endregion

        #region handle Go button
        public bool inCrossfade = false;
        private void GoButton_Click(object sender, EventArgs e)
        {
            Timer goTime = new Timer();
            goTime.Interval = 25;// 1/40 Sec  (max output rate of DMX)
            goTime.Tick += new EventHandler(goTime_Tick);
            button1.Enabled = false;
            m_timer = new Stopwatch();
            if (mCList.NextCue.upFadeTime==mCList.NextCue.downFadeTime)
                inCrossfade = true;
            m_timer.Start();
            goTime.Start();
        }
        void goTime_Tick(object sender, EventArgs e)
        {
            Timer t = (sender as Timer);
            if (t.Enabled == false) { Console.WriteLine("**tick when Disabled**"); return; }
            long elapsed = m_timer.ElapsedMilliseconds;
            byte currentSceneVal = (byte)(255-Math.Min(Math.Floor( 255 * ((double)elapsed / mCList.NextCue.downFadeTime)), 255));
            byte nextSceneVal = (byte)Math.Min(Math.Floor( 255 * ((double)elapsed / mCList.NextCue.upFadeTime)), 255);

            Console.WriteLine(string.Format("Time_Tick:{0},{1} = {2}", currentSceneVal, nextSceneVal,currentSceneVal+nextSceneVal));
            
            
            if (crossfaders1.InvokeRequired)
                crossfaders1.Invoke(new Action<byte,byte>(updateFader),currentSceneVal, nextSceneVal);
            else
            {
                if (crossfaders1.NextSceneValue!=255 && crossfaders1.CurrentSceneValue ==0)
                    crossfaders1.NextSceneValue = nextSceneVal;
                else if (crossfaders1.CurrentSceneValue != 0 && crossfaders1.NextSceneValue == 255)
                    crossfaders1.CurrentSceneValue = currentSceneVal;
                else
                {
                    crossfaders1.suspendUpdates = true;
                    crossfaders1.CurrentSceneValue = currentSceneVal;
                    crossfaders1.suspendUpdates = false;
                    crossfaders1.NextSceneValue = nextSceneVal; 
                }

                
            }
            if (currentSceneVal == 0 && nextSceneVal == 255)
            {
                t.Stop();
                m_timer.Stop();
                inCrossfade = false;
                m_timer = null;//release timer
                t.Enabled = false;
                Console.WriteLine("timer stopped");
                if (mCList.NextCue.isFollowCue)
                {
                    Timer follow = new Timer();
                    follow.Interval = mCList.NextCue.followTime;
                    follow.Tick += new EventHandler(follow_Tick);
                    follow.Start();
                }
                else
                {
                    button1.Enabled = true;
                }
            }

            
        }

        void follow_Tick(object sender, EventArgs e)
        {
            if (!(sender is Timer)) return;
            (sender as Timer).Stop();
            GoButton_Click(sender, e);
        }

        #endregion

        #region handle Widget Events
        private void button3_Click(object sender, EventArgs e)
        {
            if (com.initPro((string)comboBox1.SelectedItem))
                com.sendGetWidgetParametersRequest((ushort)0);
        }

        void com_WidgetParametersReceived(object sender, WidgetParameterArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new System.Action<string>(delegate(string s) { this.Text =s; }),"Connected");
            else
                this.Text = "Connected";
        }
        void com_SerialNumberReceived(object sender, SerialNumberArgs e)
        {
            throw new NotImplementedException();
        }
        #endregion

        void SliderGroup_LabelChanged(object sender, LabelChangedArgs e)
        {
            string[] temp = Settings.Default.Labels.Split(',');
            if (temp.Length < (e.slider.Channel))
            {
                string[] temp1 = new string[e.slider.Channel];
                Array.Copy(temp, temp1, temp.Length);
                temp = temp1;
            }
            temp[e.slider.Channel - 1] = e.slider.textBox1.Text;
            Settings.Default.Labels = String.Join(",", temp);
            SliderGroup.Labels = temp;
            Settings.Default.Save();
        }
        
        
        private void trackBar1_ValueChanged(object sender, EventArgs e)// when the cross fader
        {
            //FullScale(LiveLevels, sliderGroupLive.Values, sliderGroupNext.Values, crossFaders1.Scene1Value, crossFaders1.Scene2Value);
            outputLightMix(LiveLevels,SliderGroup.Patchlist,SliderGroup.Level,
                sliderGroupLive.ChannelValues, 
                mCList.CurrentCue.channelLevels, mCList.NextCue.channelLevels,
                crossfaders1.CurrentSceneValue, crossfaders1.NextSceneValue);
        }
        
        
        /// <summary>
        /// this will fill the channel list (Output) based on the value of the crossfaders/(next& currents Scenes)
        /// as wel as the Live console.
        /// </summary>
        /// <param name="Output">a list of dimmers Output[0] should be channel#1's value</param>
        /// <param name="patchList">this is how dimmers are patched into channels patchlist[0] contains the channel that channel#1 corrisponds to</param>
        /// <param name="Live">the CHANNEL list of the live values</param>
        /// <param name="currentScene">the CHANNEL list of the current scene</param>
        /// <param name="nextScene">the CHANNEL list of the next scene</param>
        /// <param name="currentSceneVal">the scale factor(0-255) of the current scene</param>
        /// <param name="NextSceneVal">the scale factor(0-255) of the next scene</param>
        private void outputLightMix(byte[] Output, List<int> patchList, List<byte> maxLevel, byte[] Live, byte[] currentScene, byte[] nextScene, byte currentSceneVal, byte NextSceneVal)
        {
            int i;
            int max = universeSize;
            int tChannel;
            byte tLevel;
            for (i = 0; i < max; i++)
            {
                tChannel = patchList[i]-1;
                tLevel = maxLevel[i];
                if (inCrossfade) 
                    Output[i] = crossfadeScale(Live[tChannel], currentScene[tChannel],
                          nextScene[tChannel],
                          currentSceneVal, NextSceneVal, tLevel);
                else
                    Output[i] = scale(Live[tChannel], currentScene[tChannel],
                          nextScene[tChannel],
                          currentSceneVal, NextSceneVal, tLevel);
            }
            updateTextBox();
            updateOutForm();
            updateWidget();
        }
        private byte[] mixChannelVals()
        {
            // for each channel CalculateLiveLevels the Live Sliders, the current Scene and Next Scene
            byte[] Output = new byte[512]; // this is a channel List fyi (not a channel List)
            int currentTemp,nextTemp,live;
            for (int i = 1; i <= 512; i++)
            {
                currentTemp = mCList.CurrentCue[i] * crossfaders1.CurrentSceneValue / 255;
                nextTemp = mCList.NextCue[i] * crossfaders1.NextSceneValue / 255;
                live = sliderGroupLive.ChannelValues[i-1];
                Output[i-1] = (byte)Math.Min(255, Math.Max(live, currentTemp + nextTemp));
            }
            return Output;
        }
        private byte scale(byte live, byte Xval, byte Yval, byte XLevel, byte Ylevel, byte maxDimmerVal)
        {
            double xTemp = Math.Ceiling((Xval * (XLevel / 255.0) * (maxDimmerVal / 255.0)));
            double yTemp = (int)Math.Floor((Yval * (Ylevel / 255.0) * (maxDimmerVal / 255.0)));
            double liveTemp = (int)Math.Ceiling((live * (maxDimmerVal / 255.0)));
            double temp =xTemp + yTemp;
            //double temp = (xTemp > yTemp ? xTemp : yTemp);
            if (Xval == Yval) temp = Xval;

            

            return (byte)Math.Min(255,Math.Max(liveTemp, Math.Ceiling(temp)));
            //return (byte)(xTemp < yTemp ? yTemp : xTemp);
            //return (byte)(temp > 255 ? 255 : temp);
        }

        private byte crossfadeScale(byte live, byte Xval, byte Yval, byte XLevel, byte YLevel, byte maxDimmerVal)
        {
            byte temp =(byte) (Xval + Math.Floor((Yval - Xval) * YLevel/255.0));
            return (byte)(Math.Max(temp, live) * maxDimmerVal / 255.0);
        }


        private void button2_Click(object sender, EventArgs e)//record Cue
        {
            CueNumberForm c = new CueNumberForm();
            c.ShowDialog();
            if (c.DialogResult==DialogResult.Cancel)return;
            if (mCList[c.CueNum] == null)
                mCList.AddCue(new LightCue(c.CueNum, c.CueName, mixChannelVals()));
            else
                mCList[c.CueNum].channelLevels = mixChannelVals();
        }

        #region Patch Cmd
        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            Regex rx = new Regex(@"(?<channel>\d+)(@(?<channel>\d+)(@(?<level>\d+))?)?", RegexOptions.Compiled);
            Regex rx2 = new Regex(@"\d+@\d+@\d+", RegexOptions.Compiled);


            Match m = rx.Match(textBox2.Text);
            //m = rx2.Match(textBox2.Text);
            label1.Text = "channel: " + m.Groups["channel"].Value;
            label2.Text = "channel: " + m.Groups["channel"].Value;
            label3.Text = "Value: " + m.Groups["level"].Value;
        }
        private void textBox2_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                Regex rx = new Regex(@"(?<channel>\d+)(@(?<channel>\d+)(@(?<level>\d+\%?))?)?", RegexOptions.Compiled);
                Match m = rx.Match(textBox2.Text);
                if (!m.Success) { MessageBox.Show("bad string"); return; }

                try
                {
                    int d = int.Parse(m.Groups["channel"].Value);
                    int c = int.Parse(m.Groups["channel"].Value);
                    byte l;
                    if (m.Groups["level"].Value == "")
                    {
                        l = 255;
                    }
                    else if (m.Groups["level"].Value.EndsWith("%"))
                    {
                        l = (byte)(int.Parse(m.Groups["level"].Value.TrimEnd('%')) * 255/100);
                    }
                    else
                    {
                        l = byte.Parse(m.Groups["level"].Value);
                    }

                    SliderGroup.patch(d, c, l);
                    MessageBox.Show(string.Format("Patched channel {0} to channel {1} @ {2}", d, c, l));
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentException) MessageBox.Show("there was an argument Exception");
                }
            }
        }
        #endregion

        #region dimmer Cmd
        private void txtLiveCmd_TextChanged(object sender, EventArgs e)
        {
            
        }
        private void txtLiveCmd_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                //clear can be achived with 1>96@0;
                //if (txtLiveCmd.Text == "clear") { for (int i = 1; i < 512; i++) sliderGroupLive.setLevel(i, 0); return; }

                string[] cmds = txtLiveCmd.Text.Split(',');

                Regex rx = new Regex(@"(?<channel>\d{1,3})(@(?<level>\d+\%?))?", RegexOptions.Compiled);
                Regex rx2 = new Regex(@"(?<channelList>\d+((\+|\>)\d+)*)(@(?<level>\d+\%?))?", RegexOptions.Compiled);
                Regex ThruExpession = new Regex(@"(?<start>\d+)>(?<finish>\d+)", RegexOptions.Compiled);
                Regex AndExpession = new Regex(@"(?<start>\d+)\+(?<finish>\d+)", RegexOptions.Compiled);
                Regex ChannelExpression = new Regex(@"(?<start>\d+)", RegexOptions.Compiled);


                StringBuilder  err= new StringBuilder();

                List<int> SelectedChannels = new List<int>();
                int level;

                foreach (string s in cmds)
                {
                    Match mch = rx2.Match(s);
                    SelectedChannels.Clear();

                    if (!mch.Success) { err.AppendLine("{0} is not a valid cmd"); continue; }
                    #region build the List of Selected Channels
                    if (mch.Groups["channelList"].Success != true) { err.AppendLine("{0} is not a valid cmd"); continue; }
                    string channelList = mch.Groups["channelList"].Value;
                    #region deal with ranges
                    MatchCollection ranges =  ThruExpession.Matches(channelList);
                    foreach (Match m in ranges)
                    {
                        int chanStartVal = int.Parse(m.Groups["start"].Value);
                        int chanEndVal = int.Parse(m.Groups["finish"].Value);
                        // make sure that we dont get caught in an infinite loop
                        if (chanStartVal > chanEndVal) { int swapTemp = chanEndVal; chanEndVal = chanStartVal; chanStartVal = swapTemp; } 
                        for (int channel = chanStartVal ; channel <= chanEndVal; channel++)
                        {
                            if (!SelectedChannels.Contains(channel)) SelectedChannels.Add(channel);
                        }
                    }
                    #endregion
                    #region deal with single + (ands)
                    //this will give us sum duplicate channels which is why we check to make sure that !SelectedChannels.Contains()
                    //example : 1>5+10>15 will try to add 5 and 10 to the list twice (this is not a big deal

                    ranges = AndExpession.Matches(channelList);
                    foreach (Match m in ranges)
                    {
                        int chan1 = int.Parse(m.Groups["start"].Value);
                        int chan2 = int.Parse(m.Groups["finish"].Value);
                        if (!SelectedChannels.Contains(chan1)) SelectedChannels.Add(chan1);
                        if (!SelectedChannels.Contains(chan2)) SelectedChannels.Add(chan2);
                    }
                    #endregion

                    #region for good measure (also becasue the above doesnt recognize 1@255 as a valid Channel list)
                    ranges = ChannelExpression.Matches(channelList);
                    foreach (Match m in ranges)
                    {
                        int chan = int.Parse(m.Groups["start"].Value);
                        if (!SelectedChannels.Contains(chan)) SelectedChannels.Add(chan);
                    }
                    #endregion

                    #region remove Invalid Channels ( <0  || >512)

                    SelectedChannels.RemoveAll(delegate(int i) { return i < 0 || i > 512; });

                    #endregion

                    #endregion

                    if (mch.Groups["level"].Success == true)
                    {

                        #region get Channel Value

                        if (mch.Groups["level"].Value.EndsWith("%"))
                            level = (int.Parse(mch.Groups["level"].Value.TrimEnd('%')) * 255 / 100);
                        else
                            level = int.Parse(mch.Groups["level"].Value);
                        #endregion

                        foreach (int chan in SelectedChannels)
                        {
                            sliderGroupLive.setLevel(chan, (byte)level);
                        }
                    }
                    if (SelectedChannels.Count == 1) sliderGroupLive.SelectSlider(SelectedChannels[0]);

                }
                    

                /*Match m = rx.Match(txtLiveCmd.Text);
                if (!m.Success) { MessageBox.Show("bad string"); return; }

                try
                {
                    int c = int.Parse(m.Groups["channel"].Value);
                    //byte l = byte.Parse(m.Groups["level"].Value);
                    sliderGroupLive.SelectSlider(c);

                    if (m.Groups["level"].Success == false) return;

                    int l;
                    if (m.Groups["level"].Value.EndsWith("%"))
                        l = (int.Parse(m.Groups["level"].Value.TrimEnd('%')) * 255 / 100);
                    else
                        l = int.Parse(m.Groups["level"].Value);

                    if (c < 1) throw new ArgumentOutOfRangeException("channel");
                    if (l < 0 || l > 255) throw new ArgumentOutOfRangeException("level");

                    sliderGroupLive.setLevel(c, (byte)l);
                    MessageBox.Show(string.Format("channel {0} @ {1}", c, l));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
                 */
            }
        }
        /*private void txtLiveCmd_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                if (txtLiveCmd.Text == "clear") { for (int i = 1; i < 512; i++) sliderGroupLive.setLevel(i, 0); return; }
                Regex rx = new Regex(@"(?<channel>\d+)(@(?<level>\d+\%?))?", RegexOptions.Compiled);
                Match m = rx.Match(txtLiveCmd.Text);
                if (!m.Success) { MessageBox.Show("bad string"); return; }

                try
                {
                    int c = int.Parse(m.Groups["channel"].Value);
                    //byte l = byte.Parse(m.Groups["level"].Value);
                    sliderGroupLive.SelectSlider(c);

                    if (m.Groups["level"].Success  == false) return;

                    int l;
                    if (m.Groups["level"].Value.EndsWith("%"))
                        l = (int.Parse(m.Groups["level"].Value.TrimEnd('%')) * 255 / 100);
                    else
                        l = int.Parse(m.Groups["level"].Value);

                    if (c < 1) throw new ArgumentOutOfRangeException("channel");
                    if (l < 0 || l > 255) throw new ArgumentOutOfRangeException("level");

                    sliderGroupLive.setLevel(c, (byte) l);
                    MessageBox.Show(string.Format("channel {0} @ {1}", c, l));
                }
                catch (Exception ex)
                {
                        MessageBox.Show(ex.ToString());
                }
            }
        }*/
        #endregion
        
        private void sliderGroupLive_ValueChanged(object sender, EventArgs e)// when the live leves change
        {
            outputLightMix(LiveLevels,SliderGroup.Patchlist,SliderGroup.Level, sliderGroupLive.dimmerValues, 
                mCList.CurrentCue.channelLevels, mCList.NextCue.channelLevels, 
                crossfaders1.CurrentSceneValue, crossfaders1.NextSceneValue);
        }

        private void crossFaders1_SceneChanged(object sender, EventArgs e)
        {
            mCList.gotoNextCue();
            updateCueLabels();
            sliderGroupNext.ChannelValues  = mCList.NextCue.channelLevels;

        }
        private void updateCueLabels()
        {
            lblCueCurrent.Text = string.Format("Current Cue:{0}", mCList.CurrentCueNumber);
            lblCueNext.Text = string.Format("Next Cue:{0}", mCList.NextCueNumber);
            lblCuePrev.Text = string.Format("Previous Cue:{0}", mCList.PrevCueNumber);
        }

        private void txtBlindCmd_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                int cue;
                int channel;
                if (txtBlindCmd.Text[0] == 'C' || txtBlindCmd.Text[0] == 'c')
                {
                    if (int.TryParse(txtBlindCmd.Text.Substring(1), out channel))
                        sliderGroupBlind.SelectSlider(channel);
                    else
                        MessageBox.Show("not a valid channel number");
                }
                else if (int.TryParse(txtBlindCmd.Text, out cue))
                {
                    if (mCList[cue] != null)
                        loadCueIntoBlind(cue);
                    else
                    {
                        LightCue newBlankCue = LightCue.BlankCue;
                        newBlankCue.cueNumber=cue;
                        mCList.AddCue(newBlankCue);
                        loadCueIntoBlind(cue);
                    }
                }
            }
            if (e.KeyChar == (char)Keys.Escape)
            {
                txtBlindCmd.Text = "";
            }
        }

        public void loadCueIntoBlind(int num)
        {
            //if (blindCue!=null) blindCue.channelLevelsChanged -= 
            blindCue = mCList[num];
            blindCue.channelLevelsChanged+=new EventHandler(blindCue_channelLevelsChanged);
            
            if (blindCue==null) return;

            groupBox2.Text = string.Format("Cue Number: {0} - {1}", blindCue.cueNumber, blindCue.cueName);
            Console.WriteLine("before blindslider.channels=blindcue.channel:");
            Console.WriteLine(blindCue.serialize());
            sliderGroupBlind.ChannelValues = blindCue.channelLevels; 
            Console.WriteLine("after blindslider.channels=blindcue.channel:");
            Console.WriteLine(blindCue.serialize());
            
            txtCueName.Text = blindCue.cueName;
            nudDownFade.Value = blindCue.downFadeTime;
            nudUpFade.Value = blindCue.upFadeTime;
            nudFollowTime.Value = blindCue.followTime;
            chkFollow.Checked = blindCue.isFollowCue;
        }

        private void sliderGroupBlind_ValueChanged(object sender, EventArgs e)
        {
            blindCue.channelLevels = sliderGroupBlind.ChannelValues;
        }
        private void txtCueName_TextChanged(object sender, EventArgs e)
        {
            blindCue.cueName = txtCueName.Text;
        }
        private void nudUpFade_ValueChanged(object sender, EventArgs e)
        {
            blindCue.upFadeTime = (int)nudUpFade.Value;
        }
        private void nudDownFade_ValueChanged(object sender, EventArgs e)
        {
            blindCue.downFadeTime = (int)nudDownFade.Value ;
        }
        private void nudFollowTime_ValueChanged(object sender, EventArgs e)
        {
            blindCue.followTime = (int)nudFollowTime.Value ;
        }
        
        private void Form1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar==(char)Keys.F1)
            {
                tabControl1.SelectedTab = tabPageLive;
            }
        }
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            /*
            if (e.KeyCode == Keys.F1)
                tabControl1.SelectedTab = tabPageLive;
            else if (e.KeyCode == Keys.F2)
                tabControl1.SelectedTab = tabPageNext;
            else if (e.KeyCode == Keys.F3)
                tabControl1.SelectedTab = tabPageBlind;
            else if (e.KeyCode == Keys.F4)
                tabControl1.SelectedTab = tabPagePatch;
            else if (e.KeyCode == Keys.F5)
                tabControl1.SelectedTab = tabPageConnection;
            else
            {
                if (e.KeyCode == Keys.Up && tabControl1.SelectedTab == tabPageBlind)
                { }
            }
            e.Handled = false;
             */
        }

        private void txtBlindCmd_TextChanged(object sender, EventArgs e)
        {

        }

        private void cmdDeleteCue_Click(object sender, EventArgs e)
        {
            if (blindCue == null) return;
            MessageBox.Show("are you sure?", "Confirm?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1 );
            int blindNumber = blindCue.cueNumber;
            loadCueIntoBlind(mCList.getFollowingCue(blindNumber).cueNumber);
            mCList.RemoveCue(blindNumber);
            MessageBox.Show(string.Format("cue {0} deleted", blindNumber));
        }

        private void chkFollow_CheckedChanged(object sender, EventArgs e)
        {
            blindCue.isFollowCue = chkFollow.Checked;
        }

        private void toolStripButtonSaveShow_Click(object sender, EventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.AddExtension = true;
            dlg.CreatePrompt = true;
            dlg.DefaultExt = "txt";
            dlg.OverwritePrompt = true;
            dlg.Title = "Where to save the show?";
            dlg.FileOk += new CancelEventHandler(dlg_FileOk);
            dlg.ShowDialog();
            


        }

        void dlg_FileOk(object sender, CancelEventArgs e)
        {
            if (!(sender is SaveFileDialog)) return; // sanity check
            //save the show (patchList and cue List(all cues,current,prev,next) and current Live Levels and crossfader levels)
            try
            {
                SaveFileDialog dlg = (sender as SaveFileDialog);
                System.IO.StreamWriter s=new StreamWriter(dlg.FileName);
                //write version of save file
                s.WriteLine(Form1.ShowSaveVersion);
                StringBuilder Plist = new StringBuilder();
                #region construct the patchlist
                foreach (int dimmer in SliderGroup.Patchlist)
                {
                    Plist.AppendFormat("{0},", dimmer);
                }
                Plist.Remove(Plist.Length - 1, 1); // remove the extra ","
                #endregion
                s.WriteLine(Plist.ToString());
                #region construct the Live channel LevelList
                Plist = new StringBuilder();
                foreach( byte level in sliderGroupLive.ChannelValues)
                {
                    Plist.AppendFormat("{0},", level);
                }
                Plist.Remove(Plist.Length - 1, 1); // remove the extra ","
                #endregion
                s.WriteLine(Plist.ToString());
                #region construct the Current cue/NextCue/crossfaders values string
                Plist = new StringBuilder();
                Plist.AppendFormat("{0},{1},{2},{3}", mCList.CurrentCueNumber, mCList.NextCueNumber, crossfaders1.Scene1Value, crossfaders1.Scene2Value);
                #endregion
                s.WriteLine(Plist.ToString());

                #region save all Cues
                Plist=new StringBuilder();
                Plist.AppendLine(string.Format("{0}",mCList.mCues.Count));
                foreach(LightCue c in mCList.mCues)
                    Plist.AppendLine(c.serialize());
                #endregion
                s.Write(Plist.ToString());
                s.Close();
            }
            catch
            {}
        }

        void dlg_OpenFileOk(object sender, CancelEventArgs e)
        {
            OpenFileDialog dlg = (sender as OpenFileDialog);
            System.IO.StreamReader s = new StreamReader(dlg.FileName);
            int version, i, count;
            string line;
            string[] strList;
            
            if (!int.TryParse(s.ReadLine(),out version)) return;
            if (version != Form1.ShowSaveVersion) return;
            #region get the patchList
            line = s.ReadLine();
            strList = line.Split(',');
            i=0;
            foreach (string tmpStr in strList)
            {
                SliderGroup.Patchlist[i] = int.Parse(tmpStr);
                i++;
            }
            #endregion
            #region get the Live Chanel Levels
            line = s.ReadLine();
            strList = line.Split(',');
            i=1;
            foreach (string tmpStr in strList)
            {
                sliderGroupLive.setLevel(i, byte.Parse(tmpStr));
                i++;
            }
            #endregion
            #region get current CurrentCue/NextCue/Crossfader Values
            line = s.ReadLine();
            strList = line.Split(',');
            mCList.CurrentCueNumber = int.Parse(strList[0]);
            mCList.NextCueNumber = int.Parse(strList[1]);
            crossfaders1.Scene1Value = byte.Parse(strList[2]);
            crossfaders1.Scene2Value = byte.Parse(strList[3]);

            #endregion
            #region get All the cues
            mCList.mCues.Clear();

            count = int.Parse(s.ReadLine());
            for (i = 0; i < count; i++)
            {
                line = s.ReadLine();
                mCList.AddCue(new LightCue(line));
            }
            if (mCList[0] == null) mCList.AddCue(LightCue.BlankCue);
            #endregion

            s.Close();

        }

        private void toolStripButtonLoadShow_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.AddExtension = true;
            dlg.CheckFileExists = true;
            dlg.DefaultExt = "txt";
            
            dlg.Title = "what show to open";
            dlg.FileOk +=new CancelEventHandler(dlg_OpenFileOk);
            dlg.ShowDialog();
        }


        private void cmdSetNextCue_Click(object sender, EventArgs e)
        {
            CueNumberForm cnf = new CueNumberForm();
            cnf.ShowDialog();
            if (cnf.DialogResult == DialogResult.OK)
            {
                if (cnf.CueName!="") 
                {
                    if (mCList.setNextCue(cnf.CueName))
                        MessageBox.Show("cue set");
                    else
                        MessageBox.Show("no such Cue Name");
                }
                else 
                {
                    if (mCList.setNextCue(cnf.CueNum))
                        MessageBox.Show("cue set");
                    else
                        MessageBox.Show("no such Cue Number");
                }
            }
        }
        private void cmdPrevBlindCue_Click(object sender, EventArgs e)
        {
            loadCueIntoBlind(mCList.getPrecedingCue(blindCue).cueNumber);
        }
        private void cmdNextBlindCue_Click(object sender, EventArgs e)
        {
            loadCueIntoBlind(mCList.getFollowingCue(blindCue).cueNumber);
        }

        private void cmdCopyToLive_Click(object sender, EventArgs e)
        {
            sliderGroupLive.ChannelValues = (byte[])mCList.CurrentCue.channelLevels.Clone();
        }

    }
}