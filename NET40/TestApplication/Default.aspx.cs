using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using Siderite.Web.Ajax;

namespace TestApplication
{
    public partial class _Default : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Response.Filter = new UpdatePanelShrinkerFilter(Response.Filter, Page);
        }

        protected void btnPostbackClick(object sender, EventArgs e)
        {
            tbDateTime.Text = DateTime.Now.ToString();
        }
    }
}