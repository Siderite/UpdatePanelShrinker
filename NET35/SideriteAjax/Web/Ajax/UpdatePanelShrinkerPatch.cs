namespace Siderite.Web.Ajax
{
    /// <summary>
    /// Holds information for patches in <see cref="UpdatePanelShrinkerFilter"/>
    /// </summary>
    public class UpdatePanelShrinkerPatch
    {
        ///<summary>
        ///Sets the values of the Patch
        ///</summary>
        ///<param name="index1"></param>
        ///<param name="index2"></param>
        ///<param name="length1"></param>
        ///<param name="length2"></param>
        public UpdatePanelShrinkerPatch(int index1, int index2, int length1, int length2)
        {
            IndexPrevious = index1;
            IndexCurrent = index2;
            LengthPrevious = length1;
            LengthCurrent = length2;
        }

        ///<summary>
        ///Length of the part of the previously cached content that must be replaced
        ///</summary>
        public int LengthPrevious { get; set; }

        ///<summary>
        ///Length of the part of the currently rendered content which replaces
        ///</summary>
        public int LengthCurrent { get; set; }

        ///<summary>
        /// Start position of the piece to be replaced
        ///</summary>
        public int IndexPrevious { get; set; }

        ///<summary>
        ///Start position of the piece that replaces
        ///</summary>
        public int IndexCurrent { get; set; }

        ///<summary>
        /// Displays the Patch in a more readable format
        ///</summary>
        ///<returns></returns>
        public override string ToString()
        {
            return string.Format("[{0}+{1},{2}+{3}]", IndexPrevious, LengthPrevious, IndexCurrent, LengthPrevious);
        }
    }
}