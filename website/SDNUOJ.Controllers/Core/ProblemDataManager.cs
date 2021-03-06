﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Web;

using SDNUOJ.Caching;
using SDNUOJ.Configuration;
using SDNUOJ.Controllers.Exception;
using SDNUOJ.Entity;
using SDNUOJ.Logging;
using SDNUOJ.Storage.Problem;

namespace SDNUOJ.Controllers.Core
{
    /// <summary>
    /// 题目数据管理器
    /// </summary>
    internal static class ProblemDataManager
    {
        #region 评测机方法
        /// <summary>
        /// 获取题目数据真实路径
        /// </summary>
        /// <param name="pid">题目ID</param>
        /// <returns>数据真实路径，若不存在返回null</returns>
        public static String GetProblemDataRealPath(Int32 pid)
        {
            String filePath = Path.Combine(ConfigurationManager.ProblemDataPath, pid.ToString() + ".zip");

            return (File.Exists(filePath) ? filePath : null);
        }

        /// <summary>
        /// 获取题目数据最后更新日期
        /// </summary>
        /// <param name="pid">题目ID</param>
        /// <returns>最后更新日期</returns>
        public static String GetProblemDataVersion(Int32 pid)
        {
            String version = ProblemDataCache.GetProblemDataVersionCache(pid);

            if (String.IsNullOrEmpty(version))
            {
                String filePath = GetProblemDataRealPath(pid);

                if (!String.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        ProblemDataReader reader = new ProblemDataReader(filePath);
                        version = reader.LastModified;
                    }
                    catch { }
                }
            }

            return version;
        }
        #endregion

        #region 管理方法
        /// <summary>
        /// 保存题目数据文件到磁盘
        /// </summary>
        /// <param name="problemID">题目ID</param>
        /// <param name="file">上传文件</param>
        /// <returns>是否保存成功</returns>
        public static IMethodResult AdminSaveProblemData(Int32 problemID, HttpPostedFileBase file)
        {
            if (!AdminManager.HasPermission(PermissionType.ProblemManage))
            {
                throw new NoPermissionException();
            }

            if (problemID < ConfigurationManager.ProblemSetStartID)
            {
                return MethodResult.InvalidRequest(RequestType.Problem);
            }

            if (file == null)
            {
                return MethodResult.FailedAndLog("No file was uploaded!");
            }

            if (String.IsNullOrEmpty(file.FileName))
            {
                return MethodResult.FailedAndLog("Filename can not be NULL!");
            }

            FileInfo fi = new FileInfo(file.FileName);
            if (!".zip".Equals(fi.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return MethodResult.FailedAndLog("Filename is INVALID!");
            }

            if (!String.Equals(problemID.ToString(), Path.GetFileNameWithoutExtension(fi.Name)))
            {
                return MethodResult.FailedAndLog("The problem data should have the same name as the problem ID!");
            }

            if (file.ContentLength <= 0)
            {
                return MethodResult.FailedAndLog("You can not upload empty file!");
            }

            String fileNewName = problemID.ToString() + ".zip";
            String savePath = Path.Combine(ConfigurationManager.ProblemDataPath, fileNewName);
            file.SaveAs(savePath);

            ProblemDataCache.RemoveProblemDataVersionCache(problemID);

            return MethodResult.SuccessAndLog("Admin upload problem data, id = {0}", problemID.ToString());
        }

        /// <summary>
        /// 保存题目数据文件到磁盘
        /// </summary>
        /// <param name="problemID">题目ID</param>
        /// <param name="forms">Request.Forms</param>
        /// <param name="httpFiles">Request.Files</param>
        /// <returns>是否保存成功</returns>
        public static IMethodResult AdminSaveProblemData(Int32 problemID, NameValueCollection forms, HttpFileCollectionBase httpFiles)
        {
            if (!AdminManager.HasPermission(PermissionType.ProblemManage))
            {
                throw new NoPermissionException();
            }

            if (problemID < ConfigurationManager.ProblemSetStartID)
            {
                return MethodResult.InvalidRequest(RequestType.Problem);
            }

            SortedDictionary<String, String> dictData = new SortedDictionary<String, String>();

            if (httpFiles != null)
            {
                for (Int32 i = 0; i < httpFiles.Count; i++)
                {
                    HttpPostedFileBase file = httpFiles[i];

                    if (String.IsNullOrEmpty(file.FileName))
                    {
                        return MethodResult.FailedAndLog("Filename can not be NULL!");
                    }

                    FileInfo fi = new FileInfo(file.FileName);
                    if (httpFiles.GetKey(i).IndexOf("in", StringComparison.OrdinalIgnoreCase) == 0 && !".in".Equals(fi.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return MethodResult.FailedAndLog("Filename is INVALID!");
                    }

                    if (httpFiles.GetKey(i).IndexOf("out", StringComparison.OrdinalIgnoreCase) == 0 && !".out".Equals(fi.Extension, StringComparison.OrdinalIgnoreCase) && !".ans".Equals(fi.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return MethodResult.FailedAndLog("Filename is INVALID!");
                    }

                    if (file.ContentLength <= 0)
                    {
                        return MethodResult.FailedAndLog("You can not upload empty file!");
                    }

                    StreamReader sr = new StreamReader(file.InputStream);
                    dictData.Add(httpFiles.GetKey(i), sr.ReadToEnd());
                }
            }

            if (forms != null)
            {
                for (Int32 i = 0; i < forms.Count; i++)
                {
                    if (forms.GetKey(i).IndexOf("in", StringComparison.OrdinalIgnoreCase) == 0 || forms.GetKey(i).IndexOf("out", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        dictData.Add(forms.GetKey(i), forms[i]);
                    }
                }
            }

            if (dictData.Count == 0)
            {
                return MethodResult.FailedAndLog("No data was added!");
            }

            if (dictData.Count % 2 != 0)
            {
                return MethodResult.FailedAndLog("The count of uploaded data is INVALID!");
            }

            ProblemDataWriter writer = new ProblemDataWriter();

            foreach (KeyValuePair<String, String> pair in dictData)
            {
                if (pair.Key.IndexOf("in", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    String input = pair.Value;
                    String output = String.Empty;

                    if (!dictData.TryGetValue(pair.Key.ToLower().Replace("in", "out"), out output))
                    {
                        return MethodResult.FailedAndLog("The count of uploaded data is INVALID!");
                    }

                    writer.WriteData(input, output);
                }
            }

            String fileNewName = problemID.ToString() + ".zip";
            String savePath = Path.Combine(ConfigurationManager.ProblemDataPath, fileNewName);
            Byte[] data = writer.WriteTo();
            File.WriteAllBytes(savePath, data);

            ProblemDataCache.RemoveProblemDataVersionCache(problemID);

            return MethodResult.SuccessAndLog("Admin create problem data, id = {0}", problemID.ToString());
        }

        /// <summary>
        /// 保存题目数据文件到磁盘
        /// </summary>
        /// <param name="problemID">题目ID</param>
        /// <param name="problemdata">题目数据文件</param>
        /// <returns>是否保存成功</returns>
        internal static IMethodResult InternalAdminSaveProblemData(Int32 problemID, Byte[] problemdata)
        {
            if (!AdminManager.HasPermission(PermissionType.ProblemManage))
            {
                throw new NoPermissionException();
            }

            if (problemID < ConfigurationManager.ProblemSetStartID)
            {
                return MethodResult.InvalidRequest(RequestType.Problem);
            }

            String fileNewName = problemID.ToString() + ".zip";
            String savePath = Path.Combine(ConfigurationManager.ProblemDataPath, fileNewName);
            File.WriteAllBytes(savePath, problemdata);

            ProblemDataCache.RemoveProblemDataVersionCache(problemID);

            return MethodResult.Success();
        }

        /// <summary>
        /// 获取题目数据物理路径
        /// </summary>
        /// <param name="problemID">题目ID</param>
        /// <returns>题目数据物理路径</returns>
        public static IMethodResult AdminGetProblemDataDownloadPath(Int32 problemID)
        {
            if (!AdminManager.HasPermission(PermissionType.ProblemManage))
            {
                throw new NoPermissionException();
            }

            if (problemID < ConfigurationManager.ProblemSetStartID)
            {
                return MethodResult.InvalidRequest(RequestType.Problem);
            }

            String dataPath = ProblemDataManager.GetProblemDataRealPath(problemID);

            if (String.IsNullOrEmpty(dataPath))
            {
                return MethodResult.FailedAndLog("This problem doesn't have data!");
            }

            return MethodResult.SuccessAndLog<String>(dataPath, "Admin download problem data, id = {0}", problemID.ToString());
        }

        /// <summary>
        /// 删除题目数据
        /// </summary>
        /// <param name="problemID">题目ID</param>
        /// <returns>是否删除成功</returns>
        public static IMethodResult AdminDeleteProblemDataRealPath(Int32 problemID)
        {
            if (!AdminManager.HasPermission(PermissionType.ProblemManage))
            {
                throw new NoPermissionException();
            }

            if (problemID < ConfigurationManager.ProblemSetStartID)
            {
                return MethodResult.InvalidRequest(RequestType.Problem);
            }

            String dataPath = ProblemDataManager.GetProblemDataRealPath(problemID);

            if (String.IsNullOrEmpty(dataPath))
            {
                return MethodResult.FailedAndLog("This problem does not have data!");
            }

            File.Delete(dataPath);
            ProblemDataCache.RemoveProblemDataVersionCache(problemID);

            return MethodResult.SuccessAndLog("Admin delete problem data, id = {0}", problemID.ToString());
        }
        #endregion
    }
}