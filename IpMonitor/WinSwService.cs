using IniParser.Model;
using IniParser;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using ArpLookup;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Topshelf.Options;
using System.Xml.Linq;
using IniParser.Parser;
using System.Security.Policy;
using System.Web.UI.WebControls;
using Chloe;
using Chloe.Data;
using Chloe.Infrastructure;
using Chloe.SQLite;
using Microsoft.SqlServer.Server;
using Microsoft.Data.Sqlite;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Caching;
using System.Web.Caching;
using CefSharp.OffScreen;
using CefSharp;
using CefSharp.DevTools.DOM;

namespace IpMonitor {
    internal class WinSwService {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private RestClient restClient;
        private FileIniDataParser iniDataParser;
        private IDbContext context;
        private FileCache cache;
        private readonly Timer _timer;
        private ChromiumWebBrowser browser;
        private string host = "";
        private string password = "";
        private string stok = "";

        
        public WinSwService() {
            iniDataParser = new FileIniDataParser();
            restClient = new RestClient();
            cache = new FileCache();
            context = new SQLiteContext(new DbConnectionFactory(() => {
                return new SqliteConnection(string.Format("Data Source={0};","data.db"));
            }));

            browser = new ChromiumWebBrowser("https://www.baidu.com/");
            browser.ConsoleMessage -= Browser_ConsoleMessage;
            this._timer = new Timer();
            this._timer.BeginInit();
            this._timer.Enabled = true;
            this._timer.Interval = 1;
            this._timer.Elapsed += new ElapsedEventHandler(OnElapsedTime);
            this._timer.EndInit();
        }

        private void Browser_ConsoleMessage(object sender, ConsoleMessageEventArgs e) {
            Logger.Info("Browser_ConsoleMessage "+e.Message);
        }

        public void Start() {
            _timer.Start();
        }

        public void Stop() {
            _timer.Stop();
        }

        /// <summary>
        /// 定时触发此方法,用来启动指定程序
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void OnElapsedTime(object source, ElapsedEventArgs e) {
            if (this._timer.Interval == 1) {
                this._timer.Interval = 1000 * 10;
            }
            Logger.Info("------------------------------------------");
            var readFile = iniDataParser.ReadFile("config.ini");
            this.host = readFile["miwifi"]["host"];
            this.password = readFile["miwifi"]["pwd"];
            this.stok = readFile["miwifi"]["stok"];

            var task = devicelist();
            var taskResult = task.Result;
            Logger.Info("deviceList:{0}",JsonConvert.SerializeObject(taskResult));
            if (taskResult==null) {
                string msg = string.Format("请设置stok信息");
                object cached_setStokMsg = cache.Get("setStokMsg");
                if (cached_setStokMsg == null) {
                    CacheItemPolicy policy = new CacheItemPolicy();
                    policy.AbsoluteExpiration = (DateTimeOffset)DateTime.Now.AddHours(1);
                    cache.Add("setStokMsg", "test", policy);
                    pushMsg(null,msg);
                    login();
                }
                else {
                    Logger.Info("已经推送过设置信息");
                }
                return;
            }
            Logger.Info("--------");
            foreach (var ipInfo in readFile["IP"]) {
                string status = (string)cache[ipInfo.KeyName];
                bool online = string.IsNullOrEmpty(status)?false:bool.Parse(status);
                var array = taskResult.Where(a => a["ip"].Value<string>().Equals(ipInfo.Value)).ToArray();

                Logger.Info("{0} {1} {2} {3}",ipInfo.KeyName, ipInfo.Value, online, array.Length);
                if (!online && array.Length>0) {
                    cache[ipInfo.KeyName] = "true";
                    string msg = string.Format("{0} 上线 {1}", ipInfo.KeyName,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    pushMsg(ipInfo, msg);
                    try {
                        int id = (int)context.Insert<IpLog>(() => new IpLog() { Name = ipInfo.KeyName, Ip = ipInfo.Value, Type = "login", OpTime = DateTime.Now });
                    }
                    catch (Exception ex) {
                    }
                } else if(online && array.Length == 0) {
                    cache[ipInfo.KeyName] = "false";
                    string msg = string.Format("{0} 离线 {1}", ipInfo.KeyName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    pushMsg(ipInfo, msg);
                    try {
                        int id = (int)context.Insert<IpLog>(() => new IpLog() { Name = ipInfo.KeyName, Ip = ipInfo.Value, Type = "logout", OpTime = DateTime.Now });
                    } catch (Exception ex) {
                    }
                }
            }
        }

        /// <summary>
        /// 查询设备列表
        /// </summary>
        /// <returns></returns>
        private async Task<List<JObject>> devicelist() {
            
            string url = $"{host}/cgi-bin/luci/;stok={stok}/api/misystem/devicelist";
            var request = new RestRequest(url);
            // The cancellation token comes from the caller. You can still make a call without it.
            var response = await restClient.GetAsync(request);
            // $.list[*].ip[0].ip
            var respJObj = JObject.Parse(response.Content);
            int code=respJObj["code"].Value<int>();
            if (code==0) {
                List<JObject> list = new List<JObject>();
                var ipTokens = respJObj.SelectTokens("$.list[*].ip[0]");
                foreach (var ipToken in ipTokens) {
                    list.Add(ipToken.Value<JObject>());
                }
                return list;
            }
            return null;
        }

        /// <summary>
        /// WebHook推送
        /// </summary>
        /// <param name="ipInfo"></param>
        /// <param name="content"></param>
        private void pushMsg(KeyData ipInfo, string content) {
            Logger.Info("PushMessage:{0}", content);
            var readFile = iniDataParser.ReadFile("config.ini");
            string jsonBody = new JObject() {
                {"msgtype","text"},
                {"text",JObject.FromObject(new{ content})},
            }.ToString(Formatting.None);

            var weixinPublic = readFile["weixinPublic"];
            // public 群推送
            if (ipInfo != null&& weixinPublic.ContainsKey(ipInfo.KeyName)) {
                string weixinPublicWebHook = readFile["WebHook"]["weixinPublic"];
                var requestPublic = new RestRequest(weixinPublicWebHook, Method.Post);
                requestPublic.RequestFormat = DataFormat.Json;
                requestPublic.AddBody(jsonBody);
                RestResponse responsePub = restClient.Execute(requestPublic);
                Logger.Info($"推送结果：{responsePub.Content}");
            }
            // 私推送
            else {
                string weixinWebHook = readFile["WebHook"]["weixin"];
                var request = new RestRequest(weixinWebHook, Method.Post);
                request.RequestFormat = DataFormat.Json;
                request.AddBody(jsonBody);
                RestResponse response = restClient.Execute(request);
                Logger.Info($"推送结果：{response.Content}");
            }
        }
        
        /// <summary>
        /// 登录小米路由器
        /// </summary>
        private void login() {
            Logger.Info("---------login");
            if (browser.IsBrowserInitialized) {
                string script = File.ReadAllText($"{AppDomain.CurrentDomain.BaseDirectory}/login.js", Encoding.UTF8);
                script += $"getLoginInfo('{password}')";
                var javascriptResponse = browser.GetBrowser().MainFrame.EvaluateScriptAsync(script);
                JavascriptResponse jsResp = javascriptResponse.Result;
                Logger.Info("jsResp:{0}", jsResp);
                string r = jsResp.Message;
                var jObj = JObject.FromObject(jsResp.Result);
                Logger.Info(jObj);

                string nonce = jObj["nonce"].Value<string>();
                string pwd = jObj["pwd"].Value<string>();

                RestClient client = new RestClient();
                RestRequest request = new RestRequest($"{host}/cgi-bin/luci/api/xqsystem/login", Method.Post);
                request.AddHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                var values = new Dictionary<string, string>
                {
                    { "username", "admin" },
                    { "logtype", "2" },
                    { "nonce", nonce },
                    { "password", pwd },
                };
                foreach (var item in values) {
                    request.AddParameter(item.Key, item.Value, ParameterType.GetOrPost);//这种写法也是对的
                    //request.AddParameter(item.Key, item.Value);//这种写法也是对的
                    //request.AddParameter(item.Key, item.Value, "application/x-www-form-urlencoded", ParameterType.GetOrPost); //这种写法也是对的
                }
                var executeAsync = client.ExecuteAsync(request);
                executeAsync.Wait();
                var executeAsyncResult = executeAsync.Result;
                Logger.Info(executeAsyncResult.Content);
                var trnJobj=JObject.Parse(executeAsyncResult.Content);
                int code=trnJobj["code"].Value<int>();
                if(code == 0) {
                    string token= trnJobj["token"].Value<string>();
                    
                    IniData data = iniDataParser.ReadFile("config.ini");
                    data["miwifi"]["stok"] = token;
                    iniDataParser.WriteFile("config.ini",data,Encoding.UTF8);

                    pushMsg(null, $"stok已设置");
                }
            }
            else {
                Logger.Info("cef未初始化完成");
            }
        }

    }
}
