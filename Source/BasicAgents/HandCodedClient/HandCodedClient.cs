﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GridSoccer.ClientBasic;
using GridSoccer.Common;

namespace GridSoccer.HandCodedClient
{
    public class HandCodedClient : ClientBase
    {
        private int m_patience = 1000;

        private readonly bool MoveKings = false;
        private static Random rnd = new Random();


        public HandCodedClient(string teamName, int unum)
            : base("127.0.0.1", 5050, teamName, unum)
        {
            if (Program.TrainerPatience <= 0)
                m_patience = 5 * (EnvRows + EnvCols);
            else
                m_patience = Program.TrainerPatience;

            if (unum == 1)
                SetHomePosition(2, 2);
            else if (unum == 2)
                SetHomePosition(EnvRows - 1, 2);
            else if (unum == 3)
                SetHomePosition(EnvRows / 2 + 1, 4);
        }

        private ActionTypes GetMovementDirection(Position src, Position dst)
        {
            int dr = dst.Row - src.Row;
            int dc = dst.Col - src.Col;

            ActionTypes ac = ActionTypes.Hold;
            if (dr == 0 && dc == 0)
            {
                ac = ActionTypes.Hold;
            }
            else if (dr > 0 && dr >= Math.Abs(dc))
            {
                ac = ActionTypes.MoveSouth;
            }
            else if (dr < 0 && -dr >= Math.Abs(dc))
            {
                ac = ActionTypes.MoveNorth;
            }
            else if (dc > 0 && dc >= Math.Abs(dr))
            {
                ac = ActionTypes.MoveEast;
            }
            else if (dc < 0 && -dc >= Math.Abs(dr))
            {
                ac = ActionTypes.MoveWest;
            }

            return ac;
        }

        protected override SoccerAction Think()
        {
            if (Program.IsDefensive || Program.IsRandom || Program.IsOffensive)
            {
                return NonAggressivePlayer();
            }
            else
            {
                return AggressivePlayer();
            }
        }

        private int m_curEpisodeLen = 0;
        private int m_prevEpisodeNum = 0;
        protected SoccerAction NonAggressivePlayer()
        {
            // If I am ball owner
            //     If a teammate is closer than me to the goal
            //           pass to him
            //     otherwise
            //           go directly to the goal
            // If a teammate owns the ball
            //     Go toward the closest opponent to the ball
            // If an opponent owns the ball
            //     If I am the closest teammate to the ball
            //           go to towards the ball
            //     otherwise
            //           go to the opponent's goal

            // hold every other cycle

            if (Program.IsTrainer)
            {
                if (this.OurScore + this.OppScore > m_prevEpisodeNum)
                {
                    m_prevEpisodeNum = this.OurScore + this.OppScore;
                    m_curEpisodeLen = 0;
                }
                else
                {
                    m_curEpisodeLen++;
                }

                if (m_curEpisodeLen >= m_patience)
                {
                    this.EpisodeTimeoutOppFail();
                    return null;
                }
            }

            if (Program.IsRandom)
            {
                return RandomPlayer();
            }
            else if (Program.IsDefensive)
            {
                return DefensivePlayer();
            }
            else
            {
                return AggressivePlayer();
            }
        }

        private SoccerAction DefensivePlayer()
        {
            if (this.Cycle % 2 == 0)
                return new SoccerAction(ActionTypes.Hold);

            if (this.AmIBallOwner())
            {
                if (this.GetMyPosition().Col == EnvCols && GoalUpperRow <= this.GetMyPosition().Row && this.GetMyPosition().Row <= GoalLowerRow)
                    return new SoccerAction(ActionTypes.MoveEast);

                // find teammate, closest to the opponent goal
                double minDis = Double.MaxValue;
                int OGMR = (this.GoalLowerRow + this.GoalUpperRow) / 2;
                int OGMC = this.EnvCols;
                int minDisNum = 0;
                double distGoalFromMe = MathUtils.GetDistancePointFromPoint(this.GetMyPosition(), new Position(OGMR, OGMC));
                foreach (int n in this.GetAvailableTeammatesIndeces())
                {
                    double distGoalFromPlayer = MathUtils.GetDistancePointFromPoint(
                        PlayerPositions[n], new Position(OGMR, OGMC));
                    int num = this.GetPlayerUnumFromIndex(n);
                    if (minDis > distGoalFromPlayer && PlayersCanPass(GetMyPosition(), PlayerPositions[n]))
                    {
                        minDis = distGoalFromPlayer;
                        minDisNum = num;
                    }

                }
                if (distGoalFromMe > minDis)
                {
                    return new SoccerAction(ActionTypes.Pass, minDisNum);
                }
                else
                {
                    return new SoccerAction(GetMovementDirection(GetMyPosition(), new Position(OGMR, OGMC)));
                }
            }
            else if (AreWeBallOwner())
            {
                // go to your home pos
                return new SoccerAction(GetMovementDirection(GetMyPosition(),
                    new Position(MyHomePosRow, MyHomePosCol)));
            }
            else
            {
                double myDisFromBall = MathUtils.GetDistancePointFromPoint(BallPosition, new Position(MyHomePosRow, MyHomePosCol));

                if (myDisFromBall <= Program.SensitiveDistance)
                {
                    // go towards the ball
                    return new SoccerAction(GetMovementDirection(GetMyPosition(), BallPosition));
                }
                else
                {
                    // go to your home pos
                    return new SoccerAction(GetMovementDirection(GetMyPosition(),
                        new Position(MyHomePosRow, MyHomePosCol)));
                }
            }
        }

        private SoccerAction RandomPlayer()
        {
            int[] teammates = this.GetAvailableTeammatesUnums().ToArray();
            int numActions = SoccerAction.GetActionCount(this.MoveKings, teammates.Length + 1);
            int ai = rnd.Next(0, numActions);
            return SoccerAction.GetActionFromIndex(ai, this.MoveKings, this.MyUnum);
        }

        protected SoccerAction AggressivePlayer()
        {
            // If I am ball owner
            //     If a teammate is closer than me to the goal
            //           pass to him
            //     otherwise
            //           go directly to the goal
            // If a teammate owns the ball
            //     Go toward the closest opponent to the ball
            // If an opponent owns the ball
            //     If I am the closest teammate to the ball
            //           go to towards the ball
            //     otherwise
            //           go to the opponent's goal

            if (this.AmIBallOwner())
            {
                if (this.GetMyPosition().Col == EnvCols && GoalUpperRow <= this.GetMyPosition().Row && this.GetMyPosition().Row <= GoalLowerRow)
                    return new SoccerAction(ActionTypes.MoveEast);
       
                double minDis = 1000.0;
                int OGMR = (this.GoalLowerRow + this.GoalUpperRow) / 2;
                int OGMC = this.EnvCols;
                int minDisNum = 0;
                double DisGoalFromMe = MathUtils.GetDistancePointFromPoint(this.GetMyPosition(), new Position(OGMR, OGMC));
                foreach (int n in this.GetAvailableTeammatesIndeces())
                {
                    double DisGoalFromPlayer = MathUtils.GetDistancePointFromPoint(PlayerPositions[n], new Position(OGMR, OGMC));
                    int num = this.GetPlayerUnumFromIndex(n);
                    if (minDis > DisGoalFromPlayer && PlayersCanPass(GetMyPosition(), PlayerPositions[n]))
                    {
                        minDis = DisGoalFromPlayer;
                        minDisNum = num;
                    }

                }
                if (DisGoalFromMe > minDis)
                {
                    return new SoccerAction(ActionTypes.Pass, minDisNum);
                }
                else
                {
                    return new SoccerAction(GetMovementDirection(GetMyPosition(), new Position(OGMR, OGMC)));

                }
            }

            else if (AreWeBallOwner())
            {
                int index = -1;
                double MinOppDisFromBall = double.MaxValue;
                foreach (int n in this.GetAvailableOpponentsIndeces())
                {
                    double OppDisFromBall = MathUtils.GetDistancePointFromPoint(BallPosition, PlayerPositions[n]);
                    if (MinOppDisFromBall > OppDisFromBall)
                    {
                        MinOppDisFromBall = OppDisFromBall;
                        index = n;
                    }

                }
                if (index != -1)
                {
                    return new SoccerAction(GetMovementDirection(GetMyPosition(), PlayerPositions[index]));
                }
                else
                {
                    return new SoccerAction(ActionTypes.Hold);
                }
            }
            else
            {
               
                double myDisFromBall = MathUtils.GetDistancePointFromPoint(BallPosition,GetMyPosition());
                foreach (int n in this.GetAvailableTeammatesIndeces())
                {
                    double ourDisFromBall = MathUtils.GetDistancePointFromPoint(BallPosition, PlayerPositions[n]);
                    if (myDisFromBall > ourDisFromBall)
                    {
                      
                        int OGMR = (this.GoalLowerRow + this.GoalUpperRow) / 2;
                        int OGMC = this.EnvCols;
                        return new SoccerAction(GetMovementDirection(GetMyPosition(), new Position(OGMR, OGMC)));
                    }


                }
                return new SoccerAction(GetMovementDirection(GetMyPosition(),BallPosition));
                
            }

            //return new SoccerAction(ActionTypes.Hold);
        }

        private bool PlayersCanPass(Position pos1, Position pos2)
        {
            int dr = Math.Abs(pos1.Row - pos2.Row);
            int dc = Math.Abs(pos1.Col - pos2.Col);
            return dr <= EnvPassDistance && dc <= EnvPassDistance; 
        }

    }
}
