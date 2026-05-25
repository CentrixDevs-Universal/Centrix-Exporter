using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Data.SqlClient;
using System.Net;
using WinSCP;

namespace SAPAutoSync_1._0
{
    class Program
    {
        //private static string itemsQuery;
        private static string accountsQuery;
        private static string customerQuery;
        private static string stockQuery;
        private static string server;
        private static string database;
        private static string sqlUser;
        private static string sqlPsw;
        private static SqlConnection conn = null;
        private static SqlDataReader reader = null;

        public static string fileLocation;
        public static string ftpUploadUrl;
        public static string ftpDownloadUrl;
        public static string ftpUserName;// = "Account_5814983";
        public static string ftpPassword;// = "iNfHVQ34";
        public static string companyId;// CompanyId
        public static string delimeter;
        public static string companyName;
        private static string key1;
        private static string callUrl;
        private static int wait;
        private static int timeOut;
        private static string rootPath;
        private static bool all;
        private static string error;
        private static string exportCode = "1000001";
        private static string instance;

        static void Main(string[] args)
        {
            error = "";

            try
            {

                //args = new string[3];
                //args[0] = "true";
                //args[1] = @"C:\CentrixImport\Exporter";
                ////args[2] = "SpecialPrices";
                //args[2] = "Balances";
                int numQueries = 0;

                try
                {
                    all = bool.Parse(args[0]);
                    numQueries = args.Length;

                    rootPath = args[1];
                }
                catch
                {
                    all = true;
                    rootPath = @"C:\CentrixImport\Exporter";
                }

                instance = rootPath.Substring(rootPath.LastIndexOf('\\') + 1, rootPath.Length - rootPath.LastIndexOf('\\') - 1);

                rootPath = rootPath + "\\";

                string path = rootPath + "Queries";
                //Create directories if they do not exist
                if (Directory.Exists(path) == false)
                {
                    System.IO.Directory.CreateDirectory(path);
                }

                path = rootPath + "Export";
                deleteOldFiles(path);

                path = rootPath + "Export\\Archive";
                //Create directories if they do not exist
                if (Directory.Exists(path) == false)
                {
                    System.IO.Directory.CreateDirectory(path);
                }

                path = rootPath + "Data";
                //Create directories if they do not exist
                if (Directory.Exists(path) == false)
                {
                    System.IO.Directory.CreateDirectory(path);
                }


                path = rootPath + "Errors";
                //Create directories if they do not exist
                if (Directory.Exists(path) == false)
                {
                    System.IO.Directory.CreateDirectory(path);
                }

                Console.WriteLine("Reading data file");
                readDataFile();

                int ok = checkLicense(companyName, database, "99807446"); //Check the License
                DateTime currentTime = System.DateTime.Now;

                if (ok == 0)
                {
                    Console.WriteLine("Connecting to SQL database " + database);
                    connectToSQL();

                    //delete files from Export folder
                    Console.WriteLine("Deleting old files...");
                    //********
                    path = rootPath + @"Export\";
                    deleteOldFilesDays(path, -21);

                    path = rootPath + @"Export\Archive\";
                    deleteOldFilesDays(path, -21);
                    //string[] filePaths = Directory.GetFiles(path = rootPath + @"Export\");
                    //foreach (string filePath in filePaths)
                    //    File.Delete(filePath);
                    //*******

                    //Check exportStatus table
                    //if 0 run
                    //if 1 then check time of last export
                    // if > 1 min then run
                    bool runExport = true;
                    string query;
                    try
                    {
                        query = @"SELECT MAX([Code]) + 1 'Code'
                                  ,[Instance]
                                  ,[Status]
                                  ,convert(nvarchar(20),[Date],120)  'Date'
                                  ,[Source]
                              FROM[CentrixSAP].[dbo].[CENTRIX_ExportLog]  
                                where code = (SELECT  MAX(A2.[Code]) FROM [CentrixSAP].[dbo].[CENTRIX_ExportLog] A2)
                                group by [Instance]
                                  ,[Status]
                                  ,[Date]
                                  ,[Source]";


                        query = query.Replace("\n", String.Empty);
                        query = query.Replace("\r", String.Empty);
                        query = query.Replace("\t", String.Empty);

                        List<string> exportLogValues = runSQLQuery(query);


                        if (exportLogValues.Count > 0)
                        {
                            exportCode = exportLogValues[0];
                            string status = exportLogValues[2];

                            if (status.Trim().ToUpper() == "RUNNING")
                            {
                                //Check last import date time
                                DateTime lastImport = DateTime.ParseExact(exportLogValues[3], "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                                //MyDateConversion(, "yyyy-MM-dd hh:mm:ss");


                                var diffInSeconds = (currentTime - lastImport).TotalSeconds;
                                // System.TimeSpan timeDiff = currentTime.Subtract(lastImport);
                                if (diffInSeconds < 35)
                                {
                                    //message
                                    Console.WriteLine("Export currently running, try again later.");
                                    runExport = false;
                                    System.Threading.Thread.Sleep((3 * 1000));
                                }
                            }
                            else
                            {
                                runExport = true;
                            }
                        }

                    }
                    catch
                    {
                        runExport = true;
                    }

                    if (runExport)
                    {
                        try
                        {
                            //enter log
                            query = "insert into [CentrixSAP].[dbo].[CENTRIX_ExportLog]  values (" + exportCode + ", '" + instance + "', 'Running', CONVERT(datetime, '" + currentTime.ToString("yyyy - MM - dd HH: mm:ss") + "',20), '" + System.Environment.MachineName.ToString() + "')";
                            executeSQLCommand(query);
                        }
                        catch
                        { }

                        if (all)
                        {
                            getAllQueries();
                        }
                        else
                        {
                            for (int i = 2; i < numQueries; i++)
                            {
                                getQueryFile(args[i]);
                            }
                        }

                        if (ftpUploadUrl.Trim() != "")
                        {
                            upload();
                        }

                        if (callUrl.Trim() != "")
                        {
                            if (wait > 0)
                            {
                                Console.WriteLine("Waiting for " + wait + " seconds...");
                                System.Threading.Thread.Sleep((wait * 1000));
                            }
                            Console.WriteLine("CallUrl: " + callUrl);
                            callTheUrl();
                        }

                        Console.WriteLine("Done");
                        System.Threading.Thread.Sleep((5 * 1000));
                    }
                }
                else
                {
                    Console.WriteLine("License Error!");
                    System.Threading.Thread.Sleep((5000));
                }

            }
            catch (Exception e)
            {
                error = e.ToString();
                Console.WriteLine(e.ToString());

            }


            if (error.Trim() != "")
            {
                printError();
            }

            try
            {
                //enter log
                string query = "update [CentrixSAP].[dbo].[CENTRIX_ExportLog] set [Status] = 'Done' where Code = " + exportCode + "";
                executeSQLCommand(query);

                //delete old log
                query = "delete FROM [CentrixSAP].[dbo].[CENTRIX_ExportLog] where [date] <= dateadd(dd, -90,getdate())";
                executeSQLCommand(query);
            }
            catch
            {
            }
        }


        private static void deleteOldFilesDays(string targetPath, int days)
        {
            try
            {


                string[] files = Directory.GetFiles(targetPath);

                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    if (fi.LastAccessTime < DateTime.Now.AddDays(days))
                        fi.Delete();
                }
            }
            catch { }

        }

        public static DateTime MyDateConversion(string dateAsString, string type)
        {
            return System.DateTime.ParseExact(dateAsString, type, System.Globalization.CultureInfo.CurrentCulture);
        }

        private static void deleteOldFiles(string targetPath)
        {

            string[] files = Directory.GetFiles(targetPath);

            foreach (string file in files)
            {
                FileInfo fi = new FileInfo(file);
                fi.Delete();
            }

        }


        private static void printError()
        {
            try
            {
                //text file
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                int s = user.IndexOf('\\');
                user = user.Substring(s + 1);

                string year = System.DateTime.Now.Date.Year.ToString();
                string month = System.DateTime.Now.Date.Month.ToString().PadLeft(2, '0');
                string day = System.DateTime.Now.Date.Day.ToString().PadLeft(2, '0');
                string hour = System.DateTime.Now.Date.Hour.ToString().PadLeft(2, '0');
                string minutes = System.DateTime.Now.Date.Minute.ToString().PadLeft(2, '0');
                string path = rootPath + @"Errors\";
                string filename = path + "CoprimeExportErrors_" + year + "_" + month + "_" + day + "_" + hour + "_" + minutes + ".txt";

                //write to text file
                FileInfo fi1 = new FileInfo(filename);
                using (StreamWriter sw = fi1.CreateText())
                {
                    sw.WriteLine(error);
                }
            }
            catch
            { }
        }

        private static void callTheUrl()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(callUrl);
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                string res = response.StatusCode.ToString();
                Console.WriteLine(res);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }



        private static int checkLicense(string CompName, string dbname, string addon)
        {

            try
            {

                long x, y, z, x1, x2;
                string b, c, a, d;

                a = addon;
                b = CompName;
                d = dbname;
                x = b.Length;
                x1 = a.Length;
                x2 = d.Length;
                c = "";
                string lic = "";

                long i1 = 0;
                long i2 = 0;
                long i3 = 0;

                for (int i = 11; i < x + 11; i++)
                {
                    char c1 = b[i - 11]; // First character in s

                    i1 = c1; // i=67 now

                    i1 = (i1 * i + (i * 3 / 2));
                }

                for (int i = 11; i < x1 + 11; i++)
                {
                    char c2 = a[i - 11];
                    i2 = c2;
                    i2 = (i2 * i + (i * 3 / 2));
                }

                for (int i = 11; i < x2 + 11; i++)
                {
                    char c3 = d[i - 11];
                    i3 = c3;
                    i3 = (i3 * i + (i * 3 / 2));
                }

                //MessageBox.Show(i1 + "" + i2 + "" + i3);

                i1 = i1 + i2 + i3;

                lic = i1 + "" + i2 + "" + i3;

                if (lic.Length > 9)
                {
                    lic = lic.Substring(0, 9);
                }


                if (lic == key1.Trim() || key1 == "CENTRIX DEMO!")
                {
                    return 0;
                }

                return -1;
            }
            catch
            {
                return -1;
            }
        }



        private static void getQueryFile(string query)
        {
            string sourcePath = rootPath + "Queries";

            string date1 = DateTime.Now.ToString("yyyyMMdd");
            string time = DateTime.Now.Hour.ToString();
            time += DateTime.Now.Minute.ToString();
            time += DateTime.Now.Second.ToString();
            date1 += "_" + time;

            string queryFile = query + "Query.txt";
            Console.WriteLine(queryFile);

            try
            {
                Console.WriteLine("Getting query");
                string queryString = getQuery(queryFile);

                queryFile = queryFile.Replace("Query.txt", "");
                queryFile = queryFile.Replace("DATE", date1);

                string fileName = rootPath + "Export\\" + queryFile + ".txt";
                Console.WriteLine("Creating file");
                Console.WriteLine("New file name: " + fileName);


                bool send = true;

                if (queryFile.Length >= 7)
                {
                    if (queryFile.Substring(0, 7) == "EXECUTE" || queryFile.Substring(0, 7) == "ZEXECUT")
                    {
                        Console.WriteLine("Executing " + fileName);
                        executeSQLCommand(queryString);

                        send = false;
                    }
                }

                if (send)
                {
                    createFile(fileName, queryString);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

        }




        private static void getAllQueries()
        {
            string sourcePath = rootPath + "Queries";

            string date1 = DateTime.Now.ToString("yyyyMMdd");
            string time = DateTime.Now.Hour.ToString();
            time += DateTime.Now.Minute.ToString();
            time += DateTime.Now.Second.ToString();
            date1 += "_" + time;

            if (System.IO.Directory.Exists(sourcePath))
            {
                string[] files = System.IO.Directory.GetFiles(sourcePath);

                int fileCount = 0;

                // Copy the files and overwrite destination files if they already exist.
                foreach (string s in files)
                {
                    // Use static Path methods to extract only the file name from the path.
                    string queryFile = System.IO.Path.GetFileName(s);

                    //if the file belongs to this company database
                    fileCount++;
                    Console.WriteLine(queryFile);

                    try
                    {
                        Console.WriteLine("Getting query");
                        string query = getQuery(queryFile);

                        queryFile = queryFile.Replace("Query.txt", "");
                        queryFile = queryFile.Replace("DATE", date1);

                        string fileName = rootPath + "Export\\" + queryFile + ".txt";
                        Console.WriteLine("Creating file");
                        Console.WriteLine("New file name: " + fileName);


                        bool send = true;

                        if (queryFile.Length >= 7)
                        {
                            if (queryFile.Substring(0, 7) == "EXECUTE" || queryFile.Substring(0, 7) == "ZEXECUT")
                            {
                                Console.WriteLine("Executing " + fileName);
                                executeSQLCommand(query);

                                send = false;
                            }
                        }

                        if (send)
                        {
                            createFile(fileName, query);
                        }

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }

                }

                if (fileCount == 0)
                {
                    Console.WriteLine("There are no query files");
                }


            }
        }



        private static void createFile(string fileName, string query)
        {
            try
            {
                if (conn.State == System.Data.ConnectionState.Closed)
                {
                    conn.Open();
                }

                SqlCommand cmd1 = new SqlCommand(query, conn);
                cmd1.CommandTimeout = timeOut;
                // get data stream
                reader = cmd1.ExecuteReader();

                int lineCount = 0;
                while (reader.Read())//Go through lines
                {
                    lineCount++;
                }

                reader.Close();

                Console.WriteLine(lineCount.ToString());

                if (lineCount > 0)
                {
                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.CommandTimeout = timeOut;
                    // get data stream
                    reader = cmd.ExecuteReader();


                    int i = 0;
                    string headers = "";
                    string lines = "";
                    using (StreamWriter sw = new StreamWriter(fileName, true, Encoding.UTF8))
                    {

                        while (reader.Read())//Go through lines
                        {
                            lines = "";

                            //Go through columns and get headers and data
                            for (int x = 0; x < reader.FieldCount; x++)
                            {
                                if (i == 0)//if it is the first line add the headers
                                {
                                    if (x == 0)
                                    {
                                        headers = reader.GetName(x);
                                    }
                                    else
                                    {
                                        headers += delimeter + reader.GetName(x);
                                    }

                                }

                                if (x == 0)
                                {
                                    lines = reader[x].ToString();
                                }
                                else
                                {
                                    lines += delimeter + reader[x].ToString();
                                }
                            }

                            if (i == 0)
                            {
                                sw.WriteLine(headers);
                            }

                            sw.WriteLine(lines);
                            i++;
                        }
                    }

                    reader.Close();

                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

        }



        //private static void getAllQueries()
        //{
        //    string sourcePath = rootPath + "Queries";

        //    string date1 = DateTime.Now.ToString("yyyyMMdd");
        //    string time = DateTime.Now.Hour.ToString();
        //    time += DateTime.Now.Minute.ToString();
        //    time += DateTime.Now.Second.ToString();
        //    date1 += "_" + time;

        //    if (System.IO.Directory.Exists(sourcePath))
        //    {
        //        string[] files = System.IO.Directory.GetFiles(sourcePath);

        //        int fileCount = 0;
        //        string queryString;
        //        // Copy the files and overwrite destination files if they already exist.
        //        foreach (string s in files)
        //        {
        //            // Use static Path methods to extract only the file name from the path.
        //            string queryFile = System.IO.Path.GetFileName(s);

        //            //if the file belongs to this company database
        //            fileCount++;
        //            Console.WriteLine(queryFile);

        //            try
        //            {
        //                Console.WriteLine("Getting query");
        //                queryFile = queryFile.Replace("Query.txt", "");
        //                getQueryFile(queryFile);

        //            }
        //            catch (Exception e)
        //            {
        //                Console.WriteLine(e.ToString());
        //            }

        //        }

        //        if (fileCount == 0)
        //        {
        //             Console.WriteLine("There are no query files");
        //        }


        //    }
        //}


        private static List<string> runSQLQuery(string queryString)
        {
            List<string> values = new List<string>();
            try
            {

                //conn.Open();
                SqlCommand cmd = new SqlCommand(queryString, conn);
                //cmd.CommandTimeout = timeOut;
                // get data stream
                reader = cmd.ExecuteReader();
                int i = 0;
                //values = reader;
                while (reader.Read())//Go through lines
                {
                    values.Add(reader[0].ToString().Trim());
                    values.Add(reader[1].ToString().Trim());
                    values.Add(reader[2].ToString().Trim());
                    values.Add(reader[3].ToString().Trim());
                    values.Add(reader[4].ToString().Trim());
                    i++;
                }


                reader.Close();
                conn.Close();

            }
            catch (Exception e)
            {
                //MessageBox.Show(e.ToString());
            }
            finally
            {
                conn.Close();
            }
            return values;
        }



        private static void executeSQLCommand(string queryString)
        {
            try
            {
                if (conn.State == System.Data.ConnectionState.Closed)
                {
                    conn.Open();
                }
                SqlCommand command = new SqlCommand(queryString, conn);
                command.ExecuteNonQuery();

                if (conn.State == System.Data.ConnectionState.Open)
                {
                    conn.Close();
                }
                //conn.Open();
                connectToSQL();

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                conn.Close();
            }
        }



        private static string getQuery(string queryFile)
        {
            string queryString = "";
            try
            {
                string filename = rootPath + "Queries\\" + queryFile;

                //Create a StreamReader object
                StreamReader reader = new StreamReader(filename);

                //itemsQuery = reader.ReadToEnd();
                queryString = reader.ReadToEnd();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            return queryString;
        }



        private static void upload()
        {
            string archivePath = rootPath + @"Export\Archive";

            if (!Directory.Exists(archivePath)) //if it doesn't exist create it
            {
                DirectoryInfo di = Directory.CreateDirectory(archivePath);
            }

            string filePath1 = rootPath + @"Export\";

            int fileCount = 0;
            //Get all the files in the folder and the file count
            string[] filePath = getAllFiles(filePath1, ref fileCount);

            string file;
            int index;
            string ftpPath;
            string newFilePath;



            string date1 = DateTime.Now.ToString("yyyyMMdd");
            string time = DateTime.Now.Hour.ToString();
            time += DateTime.Now.Minute.ToString();
            time += DateTime.Now.Second.ToString();
            date1 += "_" + time;



            for (int i = 0; i < fileCount; i++)
            {
                index = filePath[i].LastIndexOf('\\');
                file = filePath[i].Substring(index + 1, (filePath[i].Length - index - 1));
                // ftpPath = ftpUploadUrl + "/" + file;
                ftpPath = file;

                Console.WriteLine("Uploading file: " + file);

                uploadToFTPServer3(filePath[i], ftpPath);

                //move file to archive
                newFilePath = archivePath + "\\" + file.Substring(0,file.Length-4) + "_" + date1 + ".txt";

                //Delete file if it already exists
                if (File.Exists(newFilePath))
                {
                    File.Delete(newFilePath);
                }


                System.IO.File.Move(filePath[i], newFilePath);
            }
        }


        private static void uploadToFTPServer2(string filePath, string ftpPath)
        {
            // Get the object used to communicate with the server.
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpPath);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Timeout = (timeOut * 1000);
            //request.UsePassive = false;
            // This example assumes the FTP site uses anonymous logon.
            request.Credentials = new NetworkCredential(ftpUserName, ftpPassword);

            // Copy the contents of the file to the request stream.
            StreamReader sourceStream = new StreamReader(filePath);

            using (var inputStream = File.OpenRead(filePath))
            using (var outputStream = request.GetRequestStream())
            {
                var buffer = new byte[1024 * 1024];
                int totalReadBytesCount = 0;
                int readBytesCount;
                while ((readBytesCount = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outputStream.Write(buffer, 0, readBytesCount);
                    totalReadBytesCount += readBytesCount;
                    var progress = totalReadBytesCount * 100.0 / inputStream.Length;
                    Console.WriteLine(((int)progress).ToString());
                    //backgroundWorker1.ReportProgress((int)progress);
                }
            }




            //byte[] fileContents = Encoding.UTF8.GetBytes(sourceStream.ReadToEnd());
            //sourceStream.Close();
            //request.ContentLength = fileContents.Length;

            //Stream requestStream = request.GetRequestStream();
            //requestStream.WriteTimeout = (timeOut * 1000);
            //requestStream.Write(fileContents, 0, fileContents.Length);
            //requestStream.Close();

            FtpWebResponse response = (FtpWebResponse)request.GetResponse();

            Console.WriteLine("Upload File Complete, status {0}", response.StatusDescription);

            response.Close();
        }


        private static void uploadToFTPServer3(string path, string file)
        {

            try
            {
                SessionOptions sessionOptions;



                sessionOptions = new SessionOptions
                {

                    Protocol = Protocol.Ftp,
                    HostName = ftpUploadUrl,
                    Password = ftpPassword,
                    PortNumber = 21,
                    UserName = ftpUserName
                    //SshHostKeyFingerprint = "ssh-rsa 1024 59:5a:c6:84:e4:65:16:c5:bf:29:48:f3:6b:0b:1f:6c"

                };


                using (Session session = new Session())
                {
                    session.Open(sessionOptions);

                    TransferOptions transfers = new TransferOptions();
                    transfers.TransferMode = TransferMode.Binary;

                    TransferOperationResult transferResult;
                    transferResult = session.PutFiles(path, file, false, transfers);

                    transferResult.Check();

                    // Print results
                    foreach (TransferEventArgs transfer in transferResult.Transfers)
                    {
                        Console.WriteLine("Upload of {0} succeeded", transfer.FileName);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e);

            }


        }




        private static void uploadToFTPServer(string filePath, string ftpPath)
        {
            // Get the object used to communicate with the server.
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpPath);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Timeout = (timeOut * 1000);
            //request.UsePassive = false;
            // This example assumes the FTP site uses anonymous logon.
            request.Credentials = new NetworkCredential(ftpUserName, ftpPassword);

            // Copy the contents of the file to the request stream.
            StreamReader sourceStream = new StreamReader(filePath);

            byte[] fileContents = Encoding.UTF8.GetBytes(sourceStream.ReadToEnd());
            sourceStream.Close();
            request.ContentLength = fileContents.Length;

            Stream requestStream = request.GetRequestStream();
            requestStream.WriteTimeout = (timeOut * 1000);
            requestStream.Write(fileContents, 0, fileContents.Length);
            requestStream.Close();

            FtpWebResponse response = (FtpWebResponse)request.GetResponse();

            Console.WriteLine("Upload File Complete, status {0}", response.StatusDescription);

            response.Close();
        }



        //private static void uploadToFTPServer(string filePath, string ftpPath)
        //{

        //    using (WebClient ftpClient = new WebClient())
        //    {
        //        ftpClient.Credentials = new System.Net.NetworkCredential(ftpUserName, ftpPassword);
        //        byte[] rawResponse = ftpClient.UploadFile(ftpPath, filePath);

        //        Console.WriteLine("Remote Response: {0}", System.Text.Encoding.ASCII.GetString(rawResponse));
        //    }

        //}



        private static string[] getAllFiles(string sourcePath, ref int fileCount)
        {
            string[] files = new string[1];

            if (System.IO.Directory.Exists(sourcePath))
            {

                //Get the files
                files = System.IO.Directory.GetFiles(sourcePath);
                fileCount = files.GetLength(0);
            }

            return files;
        }



        //private static void getFTPSyncInfo()
        //{

        //    string query = "SELECT U_Value FROM [@CENTRIX_FTP] WHERE Code = 'FTPItems'";
        //    SqlCommand cmd = new SqlCommand(query, conn);
        //    reader = cmd.ExecuteReader();
        //    reader.Read();

        //    ftpUploadUrl = reader[0].ToString();
        //    reader.Close();

        //    query = "SELECT U_Value FROM [@CENTRIX_FTP] WHERE Code = 'FTPUser'";
        //    cmd = new SqlCommand(query, conn);
        //    reader = cmd.ExecuteReader();
        //    reader.Read();

        //    ftpUserName = reader[0].ToString();
        //    reader.Close();

        //    query = "SELECT U_Value FROM [@CENTRIX_FTP] WHERE Code = 'FTPPswd'";
        //    cmd = new SqlCommand(query, conn);
        //    reader = cmd.ExecuteReader();
        //    reader.Read();

        //    ftpPassword = reader[0].ToString();
        //    reader.Close();
        //}

        //private static void createFile(string fileName, string queryString)
        //{
        //    try
        //    {

        //        //conn.Open();
        //        SqlCommand cmd = new SqlCommand(queryString, conn);
        //        cmd.CommandTimeout = timeOut;
        //        // get data stream
        //        reader = cmd.ExecuteReader();

        //        bool send = false;

        //        int i = 0;
        //        string headers = "";
        //        string lines = "";
        //        using (StreamWriter sw = new StreamWriter(fileName, true, Encoding.UTF8))
        //        {

        //            while (reader.Read())//Go through lines
        //            {
        //                lines = "";

        //                //Go through columns and get headers and data
        //                for (int x = 0; x < reader.FieldCount; x++)
        //                {
        //                    if (i == 0)//if it is the first line add the headers
        //                    {
        //                        if (x == 0)
        //                        {
        //                            headers = reader.GetName(x);
        //                        }
        //                        else
        //                        {
        //                            headers += delimeter + reader.GetName(x);
        //                        }

        //                    }

        //                    if (x == 0)
        //                    {
        //                        lines = reader[x].ToString();
        //                    }
        //                    else
        //                    {
        //                        lines += delimeter + reader[x].ToString();
        //                    }
        //                }

        //                if (i == 0)
        //                {
        //                    sw.WriteLine(headers);
        //                }

        //                sw.WriteLine(lines);
        //                i++;
        //            }
        //        }

        //        reader.Close();

        //        conn.Close();
        //    }
        //    catch (Exception e)
        //    {
        //        Console.WriteLine(e.ToString());
        //        conn.Close();
        //    }
        //    finally
        //    {
        //        conn.Close();
        //    }

        //}


        private static void connectToSQL()
        {
            // instantiate and open connection
            conn = new
                SqlConnection("Server=" + server + ";DataBase=" + database + ";User id=" + sqlUser + ";Password=" + sqlPsw + ";");
            conn.Open();
        }



        private static void readDataFile()
        {
            try
            {
                int counter = 0;
                string line;

                string filename = rootPath + "Data\\Data.txt";
                Console.WriteLine("Getting data from: " + filename);

                List<string> arr = new List<string>();

                //Create a StreamReader object
                StreamReader reader = new StreamReader(filename);

                while ((line = reader.ReadLine()) != null)
                {
                    arr.Add(line);
                    //arr[counter] = line;
                    counter++;
                }

                reader.Close();

                Console.WriteLine("Finished reading");
                Console.WriteLine("Decrypting....");
                //SQL
                server = decryptLine(arr[0]);
                database = decryptLine(arr[1]);
                sqlUser = decryptLine(arr[2]);
                sqlPsw = decryptLine(arr[3]);
                delimeter = decryptLine(arr[4]);
                companyName = decryptLine(arr[5]);
                key1 = decryptLine(arr[6]);
                ftpUploadUrl = decryptLine(arr[7]);
                ftpUserName = decryptLine(arr[8]);
                ftpPassword = decryptLine(arr[9]);
                callUrl = decryptLine(arr[10]);
                wait = int.Parse(decryptLine(arr[11]));
                timeOut = int.Parse(decryptLine(arr[12]));
                Console.WriteLine("Finished decrypting");

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

        }


        private static string decryptLine(string line)
        {
            //string lic = "4|1044.909.1035.1044.-";

            int i2Length = line.Length;

            int rem = line.IndexOf("|");
            string ssi2 = line.Substring(rem + 1, (i2Length - rem - 2));


            //string a;
            string i22 = "";
            int length;
            int count = ssi2.Length;

            for (int i = 0; i < count; i++)
            {
                length = ssi2.IndexOf('.', i);
                length = length - i;
                long i2 = long.Parse(ssi2.Substring(i, length));
                i2 = i2 / 9;
                char c2 = (char)i2;
                i22 += c2;

                i += length;
            }
            // Console.WriteLine(i22);
            return i22;


        }

    }
}
