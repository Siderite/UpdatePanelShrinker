using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Caching;
using System.Web.Configuration;
using System.Web.SessionState;
using System.Web.UI.WebControls;

namespace Siderite.Web.Handlers
{
    ///<summary>
    /// <see cref="IHttpHandler"/> used by the library to send commands asynchronously
    ///</summary>
    public class CommandHandler : IHttpHandler, IRequiresSessionState
    {
        /// <summary>
        /// holds the previous versions of the renderings
        /// </summary>
        private static Dictionary<string, string> PreviousRender
        {
            get
            {
                var previousRender = new Dictionary<string, string>();
                if (HttpRuntime.Cache != null)
                {
                    if
                        (HttpRuntime.Cache["AjaxPage_PreviousRender"] != null)
                        previousRender =
                            (Dictionary<string, string>) HttpRuntime.Cache["AjaxPage_PreviousRender"];
                    else
                    {
                        HttpRuntime.Cache.Add("AjaxPage_PreviousRender", previousRender, null,
                                              Cache.NoAbsoluteExpiration, TimeSpan.FromHours(1),
                                              CacheItemPriority.NotRemovable, null);
                    }
                }
                return previousRender;
            }
        }

        #region IHttpHandler Members

        ///<summary>
        ///Enables processing of HTTP Web requests by a custom <see cref="IHttpHandler"/> that implements the <see cref="T:System.Web.IHttpHandler"></see> interface.
        ///</summary>
        ///
        ///<param name="context">An <see cref="T:System.Web.HttpContext"></see> object that provides references to the intrinsic server objects (for example, Request, Response, Session, and Server) used to service HTTP requests. </param>
        public void ProcessRequest(HttpContext context)
        {
            if (context == null) return;
            context.Response.CacheControl = "no-cache";
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            if (context.Request["CommandName"] != null)
            {
                var cea =
                    new CommandEventArgs(context.Request["CommandName"], context.Request["CommandArgument"]);
                ProcessCommand(context, cea);
            }
        }


        ///<summary>
        ///Gets a value indicating whether another request can use the <see cref="T:System.Web.IHttpHandler"></see> instance.
        ///</summary>
        ///
        ///<returns>
        ///true if the <see cref="T:System.Web.IHttpHandler"></see> instance is reusable; otherwise, false.
        ///</returns>
        ///
        public bool IsReusable
        {
            get { return false; }
        }

        #endregion

// ReSharper disable UnusedParameter.Local
        private static void ProcessCommand(HttpContext context, CommandEventArgs cea)
// ReSharper restore UnusedParameter.Local
        {
            switch (cea.CommandName)
            {
                case "ClearUploadPanelCache":
                    {
                        PreviousRender.Remove((string) cea.CommandArgument);
                    }
                    break;
            }
        }

        ///<summary>
        /// Change the Web.Config and add this to the http handlers section
        ///</summary>
        public static void ModifyWebConfig()
        {
            Configuration configuration = WebConfigurationManager.OpenWebConfiguration("~");

            var systemWeb = (SystemWebSectionGroup) configuration.GetSectionGroup("system.web");
            if (systemWeb != null)
            {
                HttpHandlersSection httpHandlers = systemWeb.HttpHandlers;
                if (httpHandlers != null)
                {
                    if (httpHandlers.Handlers.Cast<HttpHandlerAction>().Any(action => action.Path == "CommandHandler.axd"))
                    {
                        return;
                    }
                    httpHandlers.Handlers.Add(
                        new HttpHandlerAction("CommandHandler.axd", typeof (CommandHandler).FullName, "GET,HEAD"));
                    configuration.Save();
                }
            }
        }
    }
}