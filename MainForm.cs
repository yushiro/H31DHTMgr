using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;
using System.Net.Sockets;
using H31SQLLibrary;
using System.Data.Common;

namespace H31DHTMgr
{
    ///1=movie 2=MUSIC 3=book 4=exe 5=PICTURE 6=other
    /// <summary>
    /// 枚举下载数据类型
    /// </summary>
    public enum HASHTYPE
    {
        MOVIE = 1,
        MUSIC = 2,
        BOOK = 3,
        EXE = 4,
        PICTURE = 5,
        OTHER = 6
    }


    //HASH数据结构体
    public struct HASHITEM
    {
        public Int32 hashID;     //ID
        public string hashKey;     //hash
        public DateTime recvTime;   //时间
        public DateTime updateTime;   //时间
        public string recvIp;    //IP
        public Int32 recvPort;  //端口
        public string keyContent;       //文件内容
        public string keyWords;       //分析关键词
        public long recvTimes;      //总共接收了多少次
        public long fileCnt;
        public long filetotalSize;
        public int keyType; //类型 1=movie 2=MUSIC 3=book 4=exe 5=PICTURE 6=other
    };

    //HASH数据结构体
    public struct HASHFILEITEM
    {
        public Int32 ID;     //ID
        public Int32 hashID;     //ID
        public DateTime recvTime;   //时间
        public String filename;       //文件内容
        public long filesize;
    };

    public partial class MainForm : Form
    {
        private bool recvthreadison = false;    //读取事件线程
        private bool isFirstTimeCheck = true;   //是否是第一次加载了
        private DateTime m_lastrecvtime;        //上次处理读取的时间
        private string m_localPath;             //本地文件夹路径
        private H31Down m_downLoad = new H31Down();

        private string m_readFilename="";   //读取到哪个文件了
        private long m_readPos = 0;         //读取文件到些位置了，方便下一次直接读取
        private long m_fileTotal_Len = 0;   //文件总长度

        private Dictionary<string, int> m_downOKList = new Dictionary<string, int>();//用来保存已经存储到数据库里面的HASH列表+表TYPE
        private Dictionary<string, int> m_downBadList = new Dictionary<string, int>();//用来保存下载不成功的HASH列表
        private Dictionary<string, long> m_fileOKList = new Dictionary<string, long>();//用来保存已经遍历过的文件列表
        private long m_doWorkCnt = 0;           //用来统计读取了多少行文件的问题
        private int MAX_FILEDETAIL_COUNT = 100; //每次需要读取的最大行数
        private StreamReader m_reader = null;   //读取文件指针

        private int m_nowTableID = 1;           //目前种子文件表存储到哪个表了，每个表100万数据
        private int m_nowTableCount = 0;        //目前种子文件表里面有多少条数据,超过100万就要换表
        private int MAX_PAGE_SHOW_COUN = 50;    //查询每页显示的数据条数


        public MainForm()
        {
            InitializeComponent();
        }

        #region 程序加载
        private void MainForm_Load(object sender, EventArgs e)
        {
            //读取本地文件路径,初步化日志类
            m_localPath = AppDomain.CurrentDomain.BaseDirectory;
            string tmpFolder = Path.Combine(m_localPath, "Temp");
            if (!Directory.Exists(tmpFolder))
            {
                Directory.CreateDirectory(tmpFolder);
            }
            H31Debug.LogFile = Path.Combine(Path.Combine(m_localPath, "Temp"), H31Debug.c_LogFile);

            //连接数据库
            bool dbok = H31SQL.ConnectToDBServer();

            comboBox_Type.SelectedIndex = 0;
            comboBox_OrderBy.SelectedIndex = 0;
            comboBox_Lanague.SelectedIndex = 1;
            //初始化显示查询表
            m_data.DataSource = null;
            m_data.Columns.Clear();
            m_data.RowHeadersVisible = false;
            m_data.BackgroundColor = Color.White;
            m_data.Columns.Add("ID", "ID");
            m_data.Columns.Add("HashID", "HashID");
            m_data.Columns.Add("HashKey", "Hash");
            m_data.Columns.Add("recvTime", "时间");
            m_data.Columns.Add("keyContent", "keyContent");
            m_data.Columns.Add("keyType", "keyType");
            m_data.Columns.Add("recvTimes", "recvTimes");
            m_data.Columns.Add("fileCnt", "fileCnt");
            m_data.Columns.Add("filetotalSize", "filetotalSize");
            m_data.Columns.Add("TableID", "DetailID");
            m_data.Columns[0].Width = 15;
            m_data.Columns[1].Width = 20;
            m_data.Columns[2].Visible = false;
            m_data.Columns[3].Width = 60;
            m_data.Columns[4].Width = 300;
            m_data.Columns[5].Width = 10;
            m_data.Columns[6].Width = 30;
            m_data.Columns[7].Width = 30;
            m_data.Columns[8].Width = 40;
            m_data.Columns[9].Width = 10;
        }
        #endregion

        #region 搜索加载显示数据
        /// <summary>
        /// 搜索数据库显示出来 2013-07-16
        /// </summary>
        private void ButtonSearch_Click(object sender, EventArgs e)
        {
            try
            {
                //TorrentFile my = new TorrentFile("1.torrent");
                m_data.Rows.Clear();
                int selecttype = comboBox_Type.SelectedIndex + 1;
                int ordertype = comboBox_OrderBy.SelectedIndex + 1;
                string keyword = textBox_Search.Text;
                if (keyword == "关键字")
                    keyword = "";
                int searchindex = Convert.ToInt32(textBox_SearchIndex.Text);
                int ishanzi = comboBox_Lanague.SelectedIndex;
                DataSet ds = H31SQL.GetHashPageListFromDB(selecttype,ishanzi, keyword, ordertype,searchindex, MAX_PAGE_SHOW_COUN);
                Application.DoEvents();    //让系统在百忙之中来响应其他事件
                if (ds != null)
                {
                    int cnt = ds.Tables[0].Rows.Count;
                    for (int i = cnt - 1; i >= 0; i--)
                    {
                        m_data.Rows.Add((cnt - i).ToString(), ds.Tables[0].Rows[i]["ID"].ToString(), ds.Tables[0].Rows[i]["hashKey"].ToString(), ds.Tables[0].Rows[i]["recvTime"].ToString(), ds.Tables[0].Rows[i]["keyContent"].ToString(),
                            ds.Tables[0].Rows[i]["keyType"].ToString(), ds.Tables[0].Rows[i]["recvTimes"].ToString(), ds.Tables[0].Rows[i]["fileCnt"].ToString(), FormatFileSize(Convert.ToDouble(ds.Tables[0].Rows[i]["filetotalSize"].ToString())), ds.Tables[0].Rows[i]["Detail"].ToString());
                    }
                    if (MAX_PAGE_SHOW_COUN > cnt)
                        Button_NEXTPAGE.Enabled = false;
                    else
                        Button_NEXTPAGE.Enabled = true;

                }
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn(ex.StackTrace);
            }
        }

        //向前一页查询
        private void Button_PrePAGE_Click(object sender, EventArgs e)
        {
             try
            {
               int searchindex = Convert.ToInt32(textBox_SearchIndex.Text);
                searchindex = searchindex - 1;
                if (searchindex < 0)
                    searchindex = 0;
                textBox_SearchIndex.Text = searchindex.ToString();
                ButtonSearch_Click(null, null);
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn(ex.StackTrace);
            }
        }

        //向后一页查询
        private void Button_NEXTPAGE_Click(object sender, EventArgs e)
        {
            try
            {
                int searchindex = Convert.ToInt32(textBox_SearchIndex.Text);
                searchindex = searchindex + 1;
                textBox_SearchIndex.Text = searchindex.ToString();
                ButtonSearch_Click(null, null);
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn(ex.StackTrace);
            }
        }

        /// <summary>
        /// 选中一行,然后Tips显示文件列表内容
        /// </summary>
        private void m_data_MouseClick(object sender, MouseEventArgs e)
        {
            try
            {
                DataGridView.HitTestInfo hi;
                hi = m_data.HitTest(e.X, e.Y);
                if (hi.RowIndex >= 0)
                {
                    this.toolTip1.Hide(this);
                    m_data.ClearSelection();
                    m_data.Rows[hi.RowIndex].Selected = true;
                    Point mousePos = PointToClient(MousePosition);//获取鼠标当前的位置
                    int hashid = Convert.ToInt32(m_data.Rows[hi.RowIndex].Cells["HashID"].Value.ToString());
                    int tableid = Convert.ToInt32(m_data.Rows[hi.RowIndex].Cells["TableID"].Value.ToString());
                    DataSet ds = H31SQL.GetHashFileDetail(tableid, hashid);
                    Application.DoEvents();    //让系统在百忙之中来响应其他事件
                    string tip = "";
                    if (ds != null)
                    {
                        int cnt = ds.Tables[0].Rows.Count;
                        for (int i = 0; i <cnt; i++)
                        {
                            tip = tip + ds.Tables[0].Rows[i]["filename"].ToString() + "\t" + FormatFileSize(Convert.ToDouble(ds.Tables[0].Rows[i]["filesize"].ToString())) + "\r\n";
                        }
                     }
                    this.toolTip1.Show(tip, this, mousePos);//在指定位置显示提示工具
                }
                else
                {
                }
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn(ex.StackTrace);
            }
        }
        /// <summary>
        /// 复制磁链接到剪贴板中
        /// </summary>
        private void Button_Copy_Click(object sender, EventArgs e)
        {
            this.toolTip1.Hide(this);
            if (m_data.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择一行.");
                return;
            }

            int rowindex=m_data.SelectedRows[0].Index;
            string hashkey = "magnet:?xt=urn:btih:" + m_data.Rows[rowindex].Cells["HashKey"].Value.ToString();
            Clipboard.SetDataObject(hashkey); 
        }
        /// <summary>
        /// 下载BT种子文件
        /// </summary>
        private void Button_DownBT_Click(object sender, EventArgs e)
        {
            this.toolTip1.Hide(this);
            if (m_data.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择一行.");
                return;
            }
            int rowindex = m_data.SelectedRows[0].Index;
            string hashname = m_data.Rows[rowindex].Cells["HashKey"].Value.ToString();
            int res = m_downLoad.DownLoadFileByHashToFile(hashname);
            if (res == 1)
            {
                string temppath = m_localPath + "\\Torrent\\" + hashname.Substring(hashname.Length - 2, 2)+"\\"+hashname+".torrent";
                //存在则打开
                System.Diagnostics.Process.Start("explorer.exe", temppath);
            }
            else
            {
                MessageBox.Show("下载失败.");
            }
        }
        //格式化显示文件大小G M K B
        public String FormatFileSize(Double fileSize)
        {
            if (fileSize < 0)
            {
                throw new ArgumentOutOfRangeException("fileSize");
            }
            else if (fileSize >= 1024 * 1024 * 1024)
            {
                return string.Format("{0:########0.00} GB", ((Double)fileSize) / (1024 * 1024 * 1024));
            }
            else if (fileSize >= 1024 * 1024)
            {
                return string.Format("{0:####0.00} MB", ((Double)fileSize) / (1024 * 1024));
            }
            else if (fileSize >= 1024)
            {
                return string.Format("{0:####0.00} KB", ((Double)fileSize) / 1024);
            }
            else
            {
                return string.Format("{0} bytes", fileSize);
            }
        }
        #endregion


        #region 监控线程读取文件到数据库中
        //读取配置文件
        private int ReadSetting()
        {
            try
            {
                string file = Path.Combine(m_localPath,"Setting.txt");
                if (File.Exists(file))
                {
                    StreamReader reader = new StreamReader(file, Encoding.Default);
                    while (true)
                    {
                        string str1 = reader.ReadLine();
                        if (str1 == null) break;
                        string str2 = reader.ReadLine();
                        if (str2 == null) break;
                        long readPos = Convert.ToInt32(str2);
                        m_fileOKList[str1] = readPos;
                    }
                }
                string file1 = Path.Combine(m_localPath,"BadList.txt");
                if (File.Exists(file1))
                {
                    StreamReader reader = new StreamReader(file1, Encoding.Default);
                    Int32 ticktime = System.Environment.TickCount / 1000;
                    string str1 = reader.ReadLine();
                    while (str1 != null)
                    {
                        m_downBadList[str1] = ticktime;
                        str1 = reader.ReadLine();
                    }
                    reader.Close();
                }

                string file2 = Path.Combine(m_localPath,"OKList.txt");
                if (File.Exists(file2))
                {
                    StreamReader reader = new StreamReader(file2, Encoding.Default);
                    Int32 ticktime = System.Environment.TickCount / 1000;
                    string str1 = reader.ReadLine();
                    while (str1 != null)
                    {
                        m_downOKList[str1] = ticktime;
                        str1 = reader.ReadLine();
                    }
                    reader.Close();
                }
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn("ReadSetting:" + ex.StackTrace);
            }
            return 1;
        }
        //存储目前已经处理的配置文件
        private int SaveSetting()
        {
            try
            {
                //存储读取的文件列表
                if (m_readFilename.Length > 1 && m_readPos > 0)
                    m_fileOKList[m_readFilename] = m_readPos;
                string file = Path.Combine(m_localPath,"Setting.txt");
                StreamWriter writer = new StreamWriter(file);
                foreach (string key in m_fileOKList.Keys)
                {
                    writer.WriteLine(key);
                    writer.WriteLine(m_fileOKList[key].ToString());
                }
                writer.Close();

                //存储不成功的HASH列表
                string file1 = Path.Combine(m_localPath,"BadList.txt");
                StreamWriter writer1 = new StreamWriter(file1);
                foreach (string key in m_downBadList.Keys)
                {
                    writer1.WriteLine(key);
                }
                writer1.Close();

                //存储成功的HASH列表
                string file2 = Path.Combine(m_localPath,"OKList.txt");
                StreamWriter writer2 = new StreamWriter(file2);
                foreach (string key in m_downOKList.Keys)
                {
                    writer2.WriteLine(key);
                }
                writer2.Close();
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn("SaveSetting:" + ex.StackTrace);
            }

            return 1;
        }

        //取得种子文件列表存储的地方
        private int GetDetailFileTableID()
        {
            if (m_nowTableCount > 1000000 || m_nowTableCount==0)
            {
                m_nowTableID=0;
                for (int k = 0; k < 10; k++)
                {
                    int cnt1 = H31SQL.GetHashFileTableNumberCount(k + 1);
                    if (cnt1 < 1000000)
                    {
                        m_nowTableID = k + 1;
                        m_nowTableCount = cnt1;
                        break;
                    }
                }
                if (m_nowTableID == 0)
                {
                    MessageBox.Show("10个种子文件表已经用完了，请手动创建!");
                }
            }
            return 1;
        }
        /// <summary>
        /// 开始停止读取 2013-06-16
        /// </summary>
        private void ButtonStart_Click(object sender, EventArgs e)
        {
            try
            {
                if (ButtonStart.Text == "开始")
                {
                    //将文件夹由16个变成256个
                    MoveTorrentFileToSubDir(Path.Combine(m_localPath, "Torrent"));

                    MainStatusText.Text = string.Format("准备读取:{0}", DateTime.Now.ToString());
                    recvthreadison = false;
                    isFirstTimeCheck = true;

                    ReadSetting();

                    m_nowTableCount = 0;
                    m_nowTableID = 0;
                    GetDetailFileTableID();
                    RecvTimer.Start();
                    ButtonStart.Text = "停止";
                }
                else
                {
                    ButtonStart.Text = "开始";
                    RecvTimer.Stop();
                    MainStatusText.Text = string.Format("已经停止读取:{0}", DateTime.Now.ToString());
                    recvthreadison = false;

                    SaveSetting();

                    m_downBadList.Clear();
                    if (m_reader != null)
                    {
                        m_reader.Close();
                        m_reader.Dispose();
                        m_reader = null;
                    }
                }
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn("ButtonStart:" + ex.StackTrace);
            }
        }
        #endregion

        #region 读取 2013-06-16
        /// <summary>
        /// 开始停止读取 2013-06-16
        /// </summary>
        private void RecvTimer_Tick(object sender, EventArgs e)
        {
            int counttime = 1 * 1;
            int nowhour = DateTime.Now.Hour;
            TimeSpan span = DateTime.Now - m_lastrecvtime;
            if (span.TotalSeconds < counttime && isFirstTimeCheck == false)
                return;
            isFirstTimeCheck = false;

            m_lastrecvtime = DateTime.Now;
            if (recvthreadison) return;
            recvthreadison = true;
            MainStatusText.Text = string.Format("准备读取:{0}", DateTime.Now.ToString());
            Thread inittreethread = new Thread(new ThreadStart(GetTheHashThreadData));
            inittreethread.IsBackground = true;
            inittreethread.Start();//线程开始        
        }

        /// <summary>
        /// 读取线程 2013-06-16
        /// </summary>
        private void GetTheHashThreadData()
        {
            Thread.Sleep(1000 * 1);
            MethodInvoker Inthreaddown = new MethodInvoker(GetTheDataDelegate);
            this.BeginInvoke(Inthreaddown);
            Application.DoEvents();    //让系统在百忙之中来响应其他事件
        }

        /// <summary>
        /// 读取线程主函数 2013-06-16
        /// </summary>
        private void GetTheDataDelegate()
        {
            if (recvthreadison)
            {
                //检测网络状态
                if (!isConnected())
                {
                    LogTheAction(0, 1, "网络不通,请等待.");
                    recvthreadison = false;
                    return;
                }

                try
                {
                    MainStatusText.Text = string.Format("开始读取:{0}", DateTime.Now.ToString());
                    if (m_reader==null)
                    {
                        int res2=GetOneFileDataToMDB();
                        if (m_reader != null)
                        {
                            m_reader.Close();
                            m_reader.Dispose();
                        }
                    }
                    //定位到上次文件读取的地方
                    m_reader = new StreamReader(Path.Combine(m_localPath,m_readFilename), Encoding.Default);
                    long pos = m_reader.BaseStream.Seek(m_readPos, SeekOrigin.Begin);
                    Int32 ticktime1 = System.Environment.TickCount;
                    string[] strlist = new string[100];
                    string str1 = m_reader.ReadLine();
                    int i = 0;
                    while (i < 100 && str1 != null)
                    {
                        if (str1.Length > 1)
                        {
                            if (str1.Length == 40)
                                m_readPos = m_readPos + str1.Length+1;
                            else
                                m_readPos = m_readPos + str1.Length + 4;
                            strlist[i] = str1;
                            i++;
                        }
                        str1 = m_reader.ReadLine();
                    }
                    m_reader.Close();
                    m_doWorkCnt = m_doWorkCnt + i;
                    int value1=Convert.ToInt32(m_readPos / 100);
                    MainProgressBar.Value = value1 > MainProgressBar.Maximum ? MainProgressBar.Maximum : value1;

                    Int32 ticktime2 = System.Environment.TickCount;
                    if (m_doWorkCnt % 10000 == 0)
                    {
                        SaveSetting();
                        LogTheAction(0, 1, "存储到BadList" + m_downBadList.Count.ToString() + "个成功.");
                    }
                    if (i == 0)
                    {
                        LogTheAction(0, 1,m_readFilename+ "读取文件完成");
                        m_fileOKList[m_readFilename] = m_readPos;
                        int res2 = GetOneFileDataToMDB();
                        recvthreadison = false;
                        return;
                    }
                    //开始处理那么多条HASH
                    for (int k = 0; k < i&&recvthreadison; k++)
                    {
                        HASHITEM item1 = new HASHITEM();
                        //通过正则表达式获取几种不同类型的HASH文件
                        int res11=GetHashLineContent(strlist[k], ref item1);
                        
                        if(res11==1)
                        {
                            if (m_downBadList.ContainsKey(item1.hashKey))
                                continue;

                            Int32 ticktime3 = System.Environment.TickCount;
                            int detailTableid = 0;
                            int keytype = 0;
                            int ishanzi = 0;
                            bool findexist = false;

                            //先检查本地HASH列表里面是否有,没有就查询数据库里面是否有这一条，如果没有则需要插入，如果有，则需要直接更新次数和日志表
                            if (m_downOKList.ContainsKey(item1.hashKey))
                            {
                                if (m_downOKList[item1.hashKey] > 0)
                                {
                                    findexist = true;
                                    keytype = m_downOKList[item1.hashKey] / 1000;
                                    detailTableid = m_downOKList[item1.hashKey] / 10 % 100;
                                    ishanzi = m_downOKList[item1.hashKey] % 10;
                                }
                            }
                            if(findexist==false)
                            {
                                int res2 = H31SQL.CheckHashItemExist(item1.hashKey, ref keytype, ref detailTableid, ref ishanzi);
                            }
                            if (detailTableid > 0)
                            {
                                H31SQL.UpdateHashCount(keytype, item1, detailTableid, ishanzi);
                                m_downOKList[item1.hashKey] = keytype * 1000 + detailTableid * 10 + ishanzi;
                                Int32 ticktime4 = System.Environment.TickCount;
                                //LogTheAction(2, 2, ">>>>" + item1.hashKey + "更新到数据库" + item1.keyType.ToString() + "成功" + keytype.ToString() + "TIME:" + (ticktime2 - ticktime1).ToString() + "-" + (ticktime4 - ticktime3).ToString());
                            }
                            else
                            {
                                Int32 ticktime4 = System.Environment.TickCount;
                                HASHFILEITEM[] filelist=null;
                                //首先去下载种子文件并进行读取
                                int res=GetDownAndReaHashDetail(item1.hashKey, ref item1, ref filelist);
                                if (res==0||(res==1&&item1.keyContent.Contains("�")))
                                    continue;
                                Int32 ticktime5 = System.Environment.TickCount;
                                if (filelist!=null&&res == 1 &&filelist.Length <= MAX_FILEDETAIL_COUNT)
                                {
                                    item1.keyWords = "";
                                    ishanzi = ISChineseAndEnglist(item1.keyContent);
                                    //插入新的一条HASH数据
                                    int hashID = H31SQL.AddNewHash((HASHTYPE)item1.keyType, item1, m_nowTableID,ishanzi);
                                    Int32 ticktime6 = System.Environment.TickCount;
                                    if (hashID > 0)
                                    {
                                        m_downOKList[item1.hashKey] = item1.keyType * 1000 + m_nowTableID*10+ ishanzi;
                                        int real_add = 0;
                                        //插入文件列表
                                        for (int m = 0; m < filelist.Length; m++)
                                        {
                                            filelist[m].hashID = hashID;
                                            filelist[m].recvTime = item1.recvTime;
                                            //过滤一些没有用的介绍性文件
                                            if (filelist[m].filesize == 0 || (filelist[m].filename).ToLower().Contains("Thumbs.db"))
                                                continue;
                                            int dotpos = filelist[m].filename.LastIndexOf('.');
                                            string str2 = filelist[m].filename.Substring(dotpos+1, filelist[m].filename.Length - dotpos-1);
                                            if (str2.ToLower() == "url")
                                                continue;
                                            string str3 = filelist[m].filename.Substring(filelist[m].filename.Length - 1, 1);
                                            if (str3 == "_")
                                                continue;
                                            //如果是视频就直接不需要一些文件
                                            if ((item1.keyType == (int)HASHTYPE.MOVIE||filelist.Length>30) && (str2.ToLower() == "mht" || str2.ToLower() == "html" || str2.ToLower() == "htm" || str2.ToLower() == "txt"))
                                                continue;

                                            //过滤文件名字中的网址
                                            filelist[m].filename = GetOneGoodString(filelist[m].filename);
                                            int resid = H31SQL.AddNewHashDetail(item1.keyType, filelist[m], m_nowTableID);
                                            real_add = real_add+1;
                                        }
                                        m_nowTableCount = m_nowTableCount + real_add;
                                        GetDetailFileTableID();
                                        Int32 ticktime7 = System.Environment.TickCount;
                                        LogTheAction(2, 1, ">>>>"+item1.keyContent + "插入数据库" + (item1.keyType*1000+10+ishanzi).ToString()+ "成功" + filelist.Length.ToString()+"个文件 TIME:"+
                                            (ticktime2 - ticktime1).ToString() + "-" + (ticktime4 - ticktime3).ToString() + "-" + (ticktime5 - ticktime4).ToString() + "-" + (ticktime6 - ticktime5).ToString() + "-" + (ticktime7 - ticktime6).ToString());
                                    }
                                }
                            }
                            Application.DoEvents();    //让系统在百忙之中来响应其他事件
                        }
                    }

                }
                catch (System.Exception ex)
                {
                    H31Debug.PrintLn("GetTheDataDelegate:" + ex.StackTrace);
                }
            }
            else
            {
                Thread.Sleep(100);
                isFirstTimeCheck = true;
            }
            this.Text = "已经读取"+ m_readFilename+":"+ m_doWorkCnt.ToString() + "行";
            MainStatusText.Text = string.Format("读取完成:{0}", DateTime.Now.ToString());
            recvthreadison = false;
        }

        /// <summary>
        /// 正则表达式取出内容 2013-07-16
        /// </summary>
        private int GetHashLineContent(string hashline, ref HASHITEM item1)
        {
            //从http://torrage.com/sync下载的文件解析
            if (hashline.Length < 50)
            {
                if (hashline.Length == 40)
                {
                    item1.hashKey = hashline.Trim();
                    item1.recvTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    item1.recvIp = "127.0.0.1";
                    item1.recvPort = 8080;
                    return 1;
                }
                else
                {

                }
            }
            else
            {
                //自己定义的文件格式下载
                string pattern = @"ash\[(.*)\] Time\#(.*)\# ip\:(.*)\:(.*)\.(.*)\#";
                Match usermatch = Regex.Match(hashline, pattern, RegexOptions.IgnoreCase);

                if (usermatch.Groups.Count >= 4 && recvthreadison)
                {
                    item1.hashKey = usermatch.Groups[1].Value.ToString();
                    item1.recvTime = Convert.ToDateTime(usermatch.Groups[2].Value.ToString());
                    item1.recvIp = usermatch.Groups[3].Value.ToString();
                    item1.recvPort = Convert.ToInt32(usermatch.Groups[4].Value.ToString());
                    return 1;
                }
            }
            return 0;
        }

        /// <summary>
        /// 下载种子文件并进行解析读取
        /// </summary>
        private int GetDownAndReaHashDetail(string hashname, ref HASHITEM item1, ref HASHFILEITEM[] filelist)
        {
            Int32 ticktime1 = System.Environment.TickCount;
            try
            {
                int res1 = 0;
                TorrentFile myFile = null;
                if (checkBox_Torrent.Checked)
                {
                    res1 = m_downLoad.DownLoadFileByHashToFile(hashname);
                    Application.DoEvents();    //让系统在百忙之中来响应其他事件
                    if (res1 == 1)
                    {
                        string filename = Path.Combine(Path.Combine(Path.Combine(m_localPath, "Torrent"), hashname.Substring(hashname.Length - 2, 2)),hashname + ".torrent");
                        myFile = new TorrentFile(filename);
                        Int32 ticktime2 = System.Environment.TickCount;
                        if (myFile == null || myFile.TorrentName.Length == 0)
                        {
                            m_downBadList[hashname] = System.Environment.TickCount / 1000;
                            //解析不了的文件就直接备份
                            if (checkBox_Torrent.Checked)
                            {
                                File.Move(filename, Path.Combine(Path.Combine(m_localPath, "Torrent\\BAD"), hashname + ".torrent"));
                                LogTheAction(1, 1, hashname + "下载文件不对，删除" + "-" + (ticktime2 - ticktime1).ToString());
                            }
                            return 0;
                        }
                    }

                }
                else
                {
                    //不用存储文件,直接在内存中操作
                    byte[] data = m_downLoad.DownLoadFileByHashToByte(hashname);
                    if (data != null)
                        res1 = 1;
                    myFile = new TorrentFile(data);
                }
                //如果解析成功就进行赋值返回
                if (res1 == 1)
                {
                    if (myFile == null || myFile.TorrentName.Length == 0 || myFile.TorrentFileInfo.Count == 0 || myFile.TorrentFileInfo.Count > MAX_FILEDETAIL_COUNT)
                    {
                        Int32 ticktime2 = System.Environment.TickCount;
                        LogTheAction(1, 2, hashname + ">>下载成功,但使用失败." + "-" + (ticktime2 - ticktime1).ToString());
                        m_downBadList[hashname] = System.Environment.TickCount / 1000;
                        return 0;
                    }
                    else
                    {
                        //通过文件列表对文件是哪类进行分析判断
                        item1.keyType=(int)GetHashFileKeyType(ref myFile);
                        item1.fileCnt = myFile.TorrentFileInfo.Count;
                        item1.filetotalSize = myFile.TorrentPieceLength;
                        //提取正确的文件名字,过滤一些网址信息
                        item1.keyContent = GetOneGoodString(myFile.TorrentName);
                        

                        filelist = new HASHFILEITEM[myFile.TorrentFileInfo.Count];
                        for (int m = 0; m < myFile.TorrentFileInfo.Count; m++)
                        {
                            filelist[m].filename = myFile.TorrentFileInfo[m].Path;
                            filelist[m].filesize = myFile.TorrentFileInfo[m].Length;
                        }
                        Int32 ticktime2 = System.Environment.TickCount;
                        LogTheAction(1, 3, myFile.TorrentName + ">>下载成功:" + myFile.TorrentFileInfo.Count + "-" + (ticktime2 - ticktime1).ToString());
                        return 1;
                    }
                }
                else
                {
                    Int32 ticktime2 = System.Environment.TickCount;
                    m_downBadList[hashname] = System.Environment.TickCount / 1000;
                    LogTheAction(1, 4, hashname + ">>下载失败" + "-" + (ticktime2 - ticktime1).ToString());
                }
            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn("GetHashDetail:" + ex.StackTrace);
            }
            return 0;
        }

        /// <summary>
        /// 遍历该目录下所有文件,并返回一个文件名进行读取
        /// </summary>
        private int GetOneFileDataToMDB()
        {
            DirectoryInfo filedir = new DirectoryInfo(m_localPath);
            foreach (FileInfo fileChild in filedir.GetFiles("*201*.txt"))
            {
                try
                {
                    Application.DoEvents();    //让系统在百忙之中来响应其他事件
                    string fc = fileChild.ToString();
                    //如果是同一个文件，则需要检测是否读取完成
                    if (m_fileOKList.ContainsKey(fc))
                    {
                        System.IO.FileStream file1 = new System.IO.FileStream(Path.Combine(m_localPath,fc), System.IO.FileMode.Open);
                        long len=file1.Length;
                        file1.Close();
                        if (len > m_fileOKList[fc])
                        {
                            m_readFilename = fc;
                            m_readPos = m_fileOKList[fc];

                            m_fileTotal_Len = len;
                            MainProgressBar.Maximum = Convert.ToInt32(m_fileTotal_Len / 100);
                            MainProgressBar.Value = Convert.ToInt32(m_readPos / 100);
                            this.Text = "已经读取" + m_readFilename + ":" + m_doWorkCnt.ToString() + "行";

                            return 1;
                        }
                    }
                    else
                    {
                        m_doWorkCnt = 0;
                        m_readFilename = fc;
                        m_fileOKList[fc] = 0;
                        m_readPos = 0;
                        System.IO.FileStream file1 = new System.IO.FileStream(Path.Combine(m_localPath, fc), System.IO.FileMode.Open);
                        m_fileTotal_Len = file1.Length;
                        file1.Close();
                        MainProgressBar.Maximum = Convert.ToInt32(m_fileTotal_Len / 100);
                        MainProgressBar.Value = Convert.ToInt32(m_readPos / 100);
                        this.Text = "已经读取" + m_readFilename + ":" + m_doWorkCnt.ToString() + "行";

                        return 1;
                    }

                }
                catch (Exception ex)
                {
                    H31Debug.PrintLn(ex.Message);
                }
            }
            return 0;
        }
        /// <summary>
        /// 去掉标题中的网址信息
        /// </summary>
        private string GetOneGoodString(string title)
        {
            //去掉标题中的网址信息
            string res = title;
            try
            {
                //过滤[www.*]  [bbs.*]的文件名
                string pattern = @"(\[|\@|\【|\s|\(|\{)(.*)([\w-]+://?|(www|bbs)[.])([^(\]|\@|\】|\)|\})]*)(\]|\@|\】|\)|\})";
                Match usermatch = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                if (usermatch.Groups.Count > 1)
                {
                    res = res.Replace(usermatch.Groups[0].Value.ToString(), " ");
                    res = res.Trim();
                }
                //过滤[*.com]等文件名
                pattern = @"(\[|\@|\【|\s|\(|\{)(.*)\.(com|edu|gov|mil|net|org|biz|info|name|museum|us|ca|uk|cc|me|cm)([^(\]|\@|\】|\)|\}|\s)]*)(\]|\@|\】|\)|\}|\s)";
                usermatch = Regex.Match(res, pattern, RegexOptions.IgnoreCase);
                if (usermatch.Groups.Count > 1)
                {
                    res = res.Replace(usermatch.Groups[0].Value.ToString(), " ");
                    res = res.Trim();
                }
                //过滤www.*.com    bbs.*.com文件名
                pattern = @"(www|bbs)(.*)(com|edu|gov|mil|net|org|biz|info|name|museum|us|ca|uk|cc|me|cm)";
                usermatch = Regex.Match(res, pattern, RegexOptions.IgnoreCase);
                if (usermatch.Groups.Count > 1)
                {
                    res = res.Replace(usermatch.Groups[0].Value.ToString(), " ");
                    res = res.Trim();
                }
                //如果过滤错误只有后缀留下说明有错误,直接用原来的
                if (res.Length <= 5 && res.Length<title.Length)
                {
                    res = title;
                }

            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn(ex.Message);
                res = title;
            }
            return res;
        }

        //判断是否是中文，如果是日文等，则存储到另外一个表中
        private int ISChineseAndEnglist(string title)
        {
            try
            {
                string pattern = @"[\uac00-\ud7ff]+";//判断韩语   
                Match usermatch = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                if (usermatch.Groups.Count >= 1 && usermatch.Groups[0].Value.Length >= 1)
                    return 0;

                pattern = @"[\u0800-\u4e00]+";//判断日语   
                usermatch = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                if (usermatch.Groups.Count >= 1 && usermatch.Groups[0].Value.Length >= 1)
                    return 0;

                pattern = @"[\u4e00-\u9fa5]+";//判断汉字
                usermatch = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                if (usermatch.Groups.Count >= 1 && usermatch.Groups[0].Value.Length >= 1)
                    return 1;

                //判断英文，数字
                byte[] byte_len = System.Text.Encoding.Default.GetBytes(title);
                if (byte_len.Length == title.Length)
                    return 1;

            }
            catch (System.Exception ex)
            {
                H31Debug.PrintLn(ex.Message);
            }
            return 0;
        }

        //移动种子文件到子文件夹下
        private int MoveTorrentFileToSubDir(string pathname)
        {
            DirectoryInfo filedir = new DirectoryInfo(pathname);
            foreach (DirectoryInfo NextFolder in filedir.GetDirectories())
            {
                if (NextFolder.Name.Length == 1)
                {
                    foreach (FileInfo fileChild in NextFolder.GetFiles("*.torrent"))
                    {
                        try
                        {
                            Application.DoEvents();    //让系统在百忙之中来响应其他事件
                            string fc = fileChild.ToString();
                            int finddot = fc.IndexOf('.');
                            string hashname = fc.Substring(0, finddot);
                            string pathname1 = Path.Combine(pathname, hashname.Substring(hashname.Length - 2, 2).ToUpper());
                            if (!Directory.Exists(pathname1))
                            {
                                Directory.CreateDirectory(pathname1);
                            }
                            string filename1 = Path.Combine(fileChild.DirectoryName, fc);
                            string filename2 = Path.Combine(pathname1, fc);
                            File.Move(filename1, filename2);
                        }
                        catch (Exception ex)
                        {
                            H31Debug.PrintLn(ex.Message);
                        }
                    }

                }
            }
            return 0;
        }
        #endregion

        #region 检查文件类型
        /// <summary>
        /// 检测文件类型
        /// </summary>
        private HASHTYPE GetHashFileKeyType(ref TorrentFile myFile)
        {
            HASHTYPE type1 = HASHTYPE.OTHER;
            List<HASHTYPE> typelist = new List<HASHTYPE>();
            for (int m = 0; m < myFile.TorrentFileInfo.Count;m++ )
            {
                string str1 = myFile.TorrentFileInfo[m].Path;
                //获取后缀名
                int dotIndex = str1.LastIndexOf('.');
                string extName = str1.Substring(dotIndex+1, str1.Length - dotIndex-1).ToUpper();
                if (extName == "RMVB" || extName == "AVI" || extName == "RM" || extName == "OGM" || extName == "MP4" || extName == "MKV" || extName == "FLV" || extName == "MPEG" || extName == "WMA" || extName == "ASF" || extName == "WMV" || extName == "MOV" || extName == "DAT" || extName == "BDMV"
                    || extName == "CLPI" || extName == "M2TS" || extName == "VOB" || extName == "VCD" || extName == "DSF" || extName == "MPG" || extName == "DIVX" || extName == "TS" || extName == "TP")
                {
                    type1=HASHTYPE.MOVIE;
                    typelist.Add(type1);
                    break;
                }
                else if (extName == "MP3" || extName == "MID" || extName == "APE" || extName == "AAC" || extName == "WAV" || extName == "WMA" || extName == "VOC" || extName == "FLAC"|| extName == "M4A")
                {
                    type1=HASHTYPE.MUSIC;
                }
                else if (extName == "CHM"||extName == "EPUB" || extName == "PDF" || extName == "DOC" || extName == "TXT" || extName == "DOCX"
          || extName == "PPT" || extName == "PPTX" || extName == "XLS" || extName == "XLSX" || extName == "PDG" || extName == "FB2" || extName == "CBZ")
                {
                    type1=HASHTYPE.BOOK;
                }
                else if(extName=="EXE"||extName=="BIN"||extName=="COM"||extName=="BAT"||extName=="RAR"||extName=="ZIP"||extName=="7z"||extName=="ISO"
          || extName == "MSI" || extName == "TAR" || extName == "LZH" || extName == "TGZ" || extName == "OCX" || extName == "SFV" || extName == "MDF" || extName == "DMG")
                {
                    type1=HASHTYPE.EXE;
                }
                else if(extName=="BMP"||extName=="GIF"||extName=="PNG"||extName=="JPG"||extName=="RAW"||extName=="TIF"||extName=="PSD")
                {
                    type1=HASHTYPE.PICTURE;
                }
                else
                {
                    type1=HASHTYPE.OTHER;
                }
                typelist.Add(type1);
            }
            if (typelist.Contains(HASHTYPE.MOVIE))
                type1 = HASHTYPE.MOVIE;
            else if (typelist.Contains(HASHTYPE.MUSIC))
                type1 = HASHTYPE.MUSIC;
            else if (typelist.Contains(HASHTYPE.BOOK))
                type1 = HASHTYPE.BOOK;
            else if (typelist.Contains(HASHTYPE.EXE))
                type1 = HASHTYPE.EXE;
            else if (typelist.Contains(HASHTYPE.PICTURE))
                type1 = HASHTYPE.PICTURE;
            else
                type1 = HASHTYPE.OTHER;
            return type1;
        }
        #endregion

        #region 检查网络状态
        //检测网络状态
        [DllImport("wininet.dll")]
        extern static bool InternetGetConnectedState(out int connectionDescription, int reservedValue);
        /// <summary>
        /// 检测网络状态
        /// </summary>
        private bool isConnected()
        {
            int I = 0;
            bool state = InternetGetConnectedState(out I, 0);
            return state;
        }
        #endregion

        /// <summary>
        /// 记录处理日志
        /// </summary>
        private void LogTheAction(int action, int No, string Logstr)
        {
            //如果大于1000行，则清空
            if (MainLogText.Text.Length > 10 * 1000)
                MainLogText.Text = "";
            MainLogText.Text = Logstr + "\r\n" + action.ToString() + ":" + No.ToString() + ":" + MainLogText.Text;
        }

        #region 关于程序 2013-07-06
        /// <summary>
        /// 关于程序 2013-07-06
        /// </summary>
        private void ButtonAbout_Click(object sender, EventArgs e)
        {
            About a = new About();
            a.ShowDialog();
        }
        #endregion


    }
}