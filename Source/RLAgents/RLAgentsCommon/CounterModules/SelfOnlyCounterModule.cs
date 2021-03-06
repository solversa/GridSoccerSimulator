// Copyright (c) 2009 - 2011 
//  - Sina Iravanian <sina@sinairv.com>
//  - Sahar Araghi   <sahar_araghi@aut.ac.ir>
//
// This source file(s) may be redistributed, altered and customized
// by any means PROVIDING the authors name and all copyright
// notices remain intact.
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED. USE IT AT YOUR OWN RISK. THE AUTHOR ACCEPTS NO
// LIABILITY FOR ANY DATA DAMAGE/LOSS THAT THIS PRODUCT MAY CAUSE.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GridSoccer.Common;
using System.IO;

namespace GridSoccer.RLAgentsCommon.CounterModules
{
    public class SelfOnlyCounterModule : CounterModuleBase
    {
        private int m_myUnum = -1;
        private int m_teammatesCount;
        private int m_rows;
        private int m_cols;
        private int m_actionsCount;
        private int m_ballOwnerStatesCount;

        private int m_dmUpdatesCount = 0;

        private int[, , ,] m_counterTable;

        public SelfOnlyCounterModule(int rows, int cols, int teammatesCount, int myUnum)
        {
            m_rows = rows;
            m_cols = cols;
            m_actionsCount = SoccerAction.GetActionCount(Params.MoveKings, teammatesCount);
            m_myUnum = myUnum;
            m_ballOwnerStatesCount = 3; // ball owner status (0: Me)(1: We)(2: Opp) own the ball
            m_teammatesCount = teammatesCount;

            m_counterTable = new int[
                rows, cols,              // my position 
                m_ballOwnerStatesCount,  // ball owner status (0: Me)(1: We)(2: Opp) own the ball
                m_actionsCount       // number of actions
            ];
        }

        protected override int MyUnum
        {
            get
            {
                return m_myUnum;
            }
        }

        protected override int TeammatesCount
        {
            get
            {
                return m_teammatesCount;
            }
        }

        private int GetBallOwnerIndex(State s)
        {
            if(s.AmIBallOwner)
                return 0;
            else if(s.AreWeBallOwner)
                return 1;
            else
                return 2;
        }

        public override int GetCountValue(State s, int ai)
        {
            return m_counterTable[
                s.Me.Position.Row - 1, s.Me.Position.Col - 1,
                GetBallOwnerIndex(s), ai];
        }

        protected override void IncrementCountValueBase(State s, int ai)
        {
            m_counterTable[ s.Me.Position.Row - 1, s.Me.Position.Col - 1,
                            GetBallOwnerIndex(s), ai]++;
        }

        protected override void SaveBase(TextWriter tw)
        {
            m_counterTable.SaveArrayContents(tw);
        }

        protected override void LoadBase(TextReader tr)
        {
            m_counterTable.LoadInt32ArrayContents(tr);
        }

        #region Data-Mining Stuff

        public override int NumberOfDMUpdates
        {
            get { return m_dmUpdatesCount; }
        }

        public override void PerformKCyclicNeighborQUpdate(QTableBase qtable)
        {
            m_dmUpdatesCount = 0;

            // boi for ball-owner-index
            for (int boi = 0; boi < m_ballOwnerStatesCount; ++boi)
            {
                for (int r = 0; r < m_rows; ++r)
                {
                    for (int c = 0; c < m_cols; ++c)
                    {
                        double support = GetSupport(r, c, boi);

                        // if the current state has a low support, there's a need to update the qValues
                        if (support < Params.DM.MinSupport) 
                        {
                            if (Params.DM.Method == Params.DM.MethodTypes.Averaging)
                            {
                                PerformAveraging(qtable, boi, r, c);
                            }
                            else if (Params.DM.Method == Params.DM.MethodTypes.TopQ)
                            {
                                PerformTopQ(qtable, boi, r, c);
                            }
                            else if (Params.DM.Method == Params.DM.MethodTypes.Voting)
                            {
                                PerformVoting(qtable, boi, r, c);
                            }
                        }
                    }
                }
            }
        }

        private void PerformVoting(QTableBase qtable, int boi, int r, int c)
        {
            double[] qValues = new double[m_actionsCount];
            int[] counts = new int[m_actionsCount];

            for (int i = 0; i < qValues.Length; ++i)
                qValues[i] = Double.MinValue;

            // iterate through states in k-cyclic neighbors
            foreach (var neighbor in GetKCyclicNeighbors(r, c, Params.DM.K))
            {
                // if they have enough support
                if (GetSupport(neighbor[0], neighbor[1], boi) >= Params.DM.MinSupport)
                {
                    int greedyActIndex = -1;
                    double greedyActValue = Double.MinValue;

                    // Find greedy action index and value
                    for (int ai = 0; ai < m_actionsCount; ++ai)
                    {
                        if (GetActionConfidence(neighbor[0], neighbor[1], boi, ai) >= Params.DM.MinConfidence)
                        {
                            double qValue = (double)qtable.QTableArray.GetValue(neighbor[0], neighbor[1], boi, ai);
                            if (qValue > greedyActValue)
                            {
                                greedyActValue = qValue;
                                greedyActIndex = ai;
                            }
                        }
                    }

                    if (greedyActIndex >= 0)
                    {
                        counts[greedyActIndex]++;
                        if (greedyActValue > qValues[greedyActIndex])
                            qValues[greedyActIndex] = greedyActValue;
                    }
                }
            }

            int maxCountIndex = -1;
            int maxValue = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > maxValue)
                {
                    maxCountIndex = i;
                    maxValue = counts[i];
                }
            }

            if (maxCountIndex >= 0 && maxValue > 0)
            {
                qtable.QTableArray.SetValue(qValues[maxCountIndex], r, c, boi, maxCountIndex);
                m_dmUpdatesCount++;
            }
        }

        private void PerformTopQ(QTableBase qtable, int boi, int r, int c)
        {
            double[] qValues = new double[m_actionsCount];
            int[] counts = new int[m_actionsCount];

            for (int i = 0; i < qValues.Length; ++i)
                qValues[i] = Double.MinValue;

            // iterate through states in k-cyclic neighbors
            foreach (var neighbor in GetKCyclicNeighbors(r, c, Params.DM.K))
            {
                // if they have enough support
                if (GetSupport(neighbor[0], neighbor[1], boi) >= Params.DM.MinSupport)
                {
                    // accumulate Q for high-confidence actions
                    for (int ai = 0; ai < m_actionsCount; ++ai)
                    {
                        if (GetActionConfidence(neighbor[0], neighbor[1], boi, ai) >= Params.DM.MinConfidence)
                        {
                            double qValue = (double)qtable.QTableArray.GetValue(neighbor[0], neighbor[1], boi, ai);
                            if (qValue > qValues[ai])
                                qValues[ai] = qValue;
                            counts[ai]++;
                        }
                    }
                }
            }

            // update in the q-Table
            for (int ai = 0; ai < m_actionsCount; ++ai)
            {
                if (counts[ai] > 0 && qValues[ai] != Double.MinValue)
                {
                    qtable.QTableArray.SetValue(qValues[ai], r, c, boi, ai);
                    m_dmUpdatesCount++;
                }
            }

        }


        private void PerformAveraging(QTableBase qtable, int boi, int r, int c)
        {
            double[] qValues = new double[m_actionsCount];
            int[] counts = new int[m_actionsCount];

            // iterate through states in k-cyclic neighbors
            foreach (var neighbor in GetKCyclicNeighbors(r, c, Params.DM.K))
            {
                // if they have enough support
                if (GetSupport(neighbor[0], neighbor[1], boi) >= Params.DM.MinSupport)
                {
                    // accumulate Q for high-confidence actions
                    for (int ai = 0; ai < m_actionsCount; ++ai)
                    {
                        if (GetActionConfidence(neighbor[0], neighbor[1], boi, ai) >= Params.DM.MinConfidence)
                        {
                            qValues[ai] += (double)qtable.QTableArray.GetValue(neighbor[0], neighbor[1], boi, ai);
                            counts[ai]++;
                        }
                    }
                }
            }

            // compute the mean of q-Values, and update in the q-Table
            for (int ai = 0; ai < m_actionsCount; ++ai)
            {
                if (counts[ai] > 0)
                {
                    qValues[ai] /= counts[ai];
                    qtable.QTableArray.SetValue(qValues[ai], r, c, boi, ai);
                    m_dmUpdatesCount++;
                }
            }
        }

        private List<int[]> GetKCyclicNeighbors(int r, int c, int k)
        {
            List<int[]> result = new List<int[]>();
            for (int ki = 1; ki <= k; ++ki)
            {
                int newr;
                int newc;
                
                // upper side
                newr = r - ki;
                for (newc = c - ki; newc <= c + ki; newc++)
                {
                    if (0 <= newr && newr < m_rows && 0 <= newc && newc < m_cols)
                        result.Add(new int[] { newr, newc });
                }

                // lower side
                newr = r + ki;
                for (newc = c - ki; newc <= c + ki; newc++)
                {
                    if (0 <= newr && newr < m_rows && 0 <= newc && newc < m_cols)
                        result.Add(new int[] { newr, newc });
                }

                // left side
                newc = c - ki;
                for (newr = r - ki + 1; newr <= r + ki - 1; newr++)
                {
                    if (0 <= newr && newr < m_rows && 0 <= newc && newc < m_cols)
                        result.Add(new int[] { newr, newc });
                }

                // right side
                newc = c + ki;
                for (newr = r - ki + 1; newr <= r + ki - 1; newr++)
                {
                    if (0 <= newr && newr < m_rows && 0 <= newc && newc < m_cols)
                        result.Add(new int[] { newr, newc });
                }
            }
            return result;
        }

        private double GetActionConfidence(int r, int c, int boi, int ai)
        {
            double support = GetSupport(r, c, boi);
            return ((double)m_counterTable[r, c, boi, ai]) / support;
        }

        private double GetSupport(int r, int c, int boi)
        {
            double sum = 0.0;

            for (int i = 0; i < m_actionsCount; ++i)
            {
                sum += m_counterTable[r, c, boi, i];
            }

            return sum;
        }

        #endregion
    }
}
