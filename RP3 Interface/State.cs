using System;


namespace RP3_Interface
{
    //Track and update State variables
    public class State
    {
        //angular values (instant, end, start)-> in Rower
        public float theta_start, theta_end;
        public float w_end, w_start;
        public float linearDist, linearVel;


        public State()
        {
            this.reset();
        }

        public void setStart(float t, float w)
        {
            theta_start = t;
            w_start = w;
        }

        public void setEnd(float t, float w)
        {
            theta_end = t;
            w_end = w;
        }


        public void UpdateValues()
        {
            //tbd
        }

        public void linearCalc(float k, float theta, float w) //or do this at the rower self?
        {
            linearDist = k * theta;
            linearVel = k * w;
        }

        public void reset() //reset all variables
        {
            theta_start = theta_end = 0f;
            w_start = w_end = 0f;
            //linearDist = linearVel = 0f;
        }
    }

    public class Drive : State
    {
        //for later; add energy related functions here
    }

    public class Recovery : State
    {
        public float calcDF(float I, float recTime, float currDF, bool f)
        {
            //Console.WriteLine("Time ", recTime);
            //Console.Write(string.Format("w_start : {0:0.000#####}", w_start));
            //Console.WriteLine(string.Format(" w_end : {0:0.000#####}", w_end));
            //Inertia * angular accleration / recovery time
            Console.WriteLine("df ", currDF);
            if (f == true) return currDF;
            else if (w_start <= 0 || w_end <= 0) return currDF;
            else return (I * ((1 / this.w_start) - (1 / this.w_end)) * recTime);
        }
    }
}