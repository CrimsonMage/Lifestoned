/*****************************************************************************************
Copyright 2018 Dereth Forever

Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
*****************************************************************************************/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using DerethForever.Web.Models;
using DerethForever.Web.Models.Account;
using DerethForever.Web.Models.CachePwn;
using DerethForever.Web.Models.Discord;
using DerethForever.Web.Models.Enums;
using DerethForever.Web.Models.Shared;
using DerethForever.Web.Models.Weenie;
using log4net;
using Newtonsoft.Json;
using RestSharp;

namespace DerethForever.Web.Controllers
{
    public class WeenieController : BaseController
    {
        private const string _Session_IndexModel = "__weenieIndexModel";
        private const string _Session_DownloadModel = "__weenieDownloadModel";

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public IndexModel CurrentIndexModel
        {
            get { return (IndexModel)Session[_Session_IndexModel]; }
            set { Session[_Session_IndexModel] = value; }
        }

        public DownloadChangeModel CurrentDownload
        {
            get { return (DownloadChangeModel)Session[_Session_DownloadModel]; }
            set { Session[_Session_DownloadModel] = value; }
        }

        [HttpGet]
        public ActionResult Recent()
        {
            IndexModel model = CurrentIndexModel ?? new IndexModel();
            model.Results = SandboxContentProviderHost.CurrentProvider.RecentChanges(GetUserToken());

            return View(model);
        }

        [HttpGet]
        public ActionResult Index()
        {
            IndexModel model = CurrentIndexModel ?? new IndexModel();
            BaseModel current = CurrentBaseModel;
            BaseModel.CopyBaseData(current, model);
            CurrentBaseModel = current;

            return View(model);
        }

        [HttpPost]
        public ActionResult Index(IndexModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                model.Results = SandboxContentProviderHost.CurrentProvider.WeenieSearch(GetUserToken(), model.Criteria);

                List<WeenieChange> mine = SandboxContentProviderHost.CurrentProvider.GetMyWeenieChanges(GetUserToken());
                model.Results.ForEach(w =>
                {
                    if (mine.Any(m => m.Weenie.WeenieClassId == w.WeenieClassId))
                        w.HasSandboxChange = true;
                });

                model.ShowResults = true;
            }
            catch (Exception ex)
            {
                model.Results = null;
                model.ShowResults = false;
                model.ErrorMessages.Add("Error retrieving data from the API");
                model.Exception = ex;
            }

            return View(model);
        }

        [HttpGet]
        [Authorize]
        public ActionResult Edit(uint id)
        {
            Weenie model = SandboxContentProviderHost.CurrentProvider.GetWeenie(GetUserToken(), id);
            SortTheThings(model);

            return View(model);
        }

        private void SortTheThings(Weenie model)
        {
            // Setting sort order for edit
            model.IntProperties = model.IntProperties.OrderBy(ip => ip.IntPropertyId).ToList();
            model.DoubleProperties = model.DoubleProperties.OrderBy(dp => dp.DoublePropertyId).ToList();

            model.EmoteTable.ForEach(t => t.Emotes = t.Emotes.OrderBy(e => e.SortOrder).ToList());
            model.EmoteTable = model.EmoteTable.OrderBy(e => e.EmoteCategoryId).ToList();
        }

        [HttpGet]
        [Authorize]
        public ActionResult Clone(uint id)
        {
            Weenie model = SandboxContentProviderHost.CurrentProvider.GetWeenie(GetUserToken(), id);
            model.WeenieClassId = 0;
            model.DataObjectId = 0;
            ImportedWeenie = model;
            return RedirectToAction("New");
        }

        [HttpGet]
        [Authorize]
        public ActionResult New()
        {
            Weenie model = ImportedWeenie ?? new Weenie();

            return View(model);
        }

        [HttpGet]
        [Authorize]
        public ActionResult EditImported()
        {
            Weenie model = ImportedWeenie;

            if (model == null)
                RedirectToAction("Upload");

            Weenie existing = SandboxContentProviderHost.CurrentProvider.GetWeenie(GetUserToken(), model.WeenieClassId);
            if (existing == null)
                return RedirectToAction("NewImported");

            SortTheThings(model);

            return View("Edit", model);
        }

        [HttpGet]
        [Authorize]
        public ActionResult NewImported()
        {
            Weenie model = ImportedWeenie;

            if (model == null)
                RedirectToAction("Upload");

            SortTheThings(model);

            return View("New", model);
        }

        [HttpPost]
        [Authorize]
        public ActionResult New(Weenie model)
        {
            ActionResult result = HandlePostback(model);

            if (result != null)
                return result;

            result = ValidateWeenie(model);

            if (result != null)
                return result;

            model.CleanDeletedAndEmptyProperties();
            model.LastModified = DateTime.Now;
            model.ModifiedBy = GetUserName();
            model.DataObjectId = model.WeenieClassId; // both need to be set, only 1 is editable.

            try
            {
                SandboxContentProviderHost.CurrentProvider.CreateWeenie(GetUserToken(), model);

                IndexModel indexModel = new IndexModel();
                indexModel.SuccessMessages.Add("Weenie " + model.WeenieClassId.ToString() + " successfully created.");
                CurrentIndexModel = indexModel;

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                model.ErrorMessages.Add("Error saving weenie in the API");
                model.Exception = ex;
            }

            return View(model);
        }

        [HttpPost]
        [Authorize]
        public ActionResult Edit(Weenie model)
        {
            ActionResult result = HandlePostback(model);

            if (result != null)
            {
                ModelState.Clear();
                return result;
            }

            result = ValidateWeenie(model);

            if (result != null)
            {
                ModelState.Clear();
                return result;
            }

            model.CleanDeletedAndEmptyProperties();
            model.LastModified = DateTime.Now;
            model.ModifiedBy = GetUserName();

            try
            {
                SandboxContentProviderHost.CurrentProvider.UpdateWeenie(GetUserToken(), model);

                IndexModel indexModel = new IndexModel();
                indexModel.SuccessMessages.Add("Weenie " + model.WeenieClassId.ToString() + " successfully saved.");
                CurrentIndexModel = indexModel;

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                model.ErrorMessages.Add("Error saving weenie in the API");
                model.Exception = ex;
            }

            return View(model);
        }

        private ActionResult ValidateWeenie(Weenie model)
        {
            bool isValid = true;

            // validatate no duplicate properties
            var dupeInts = model.IntProperties.GroupBy(p => p.IntPropertyId).SelectMany(s => s.Skip(1));
            if (dupeInts.Count() > 0)
            {
                isValid = false;
                dupeInts.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate Int property {dupe.IntPropertyId} - you may only have one."));
            }

            var dupeInt64s = model.Int64Properties.GroupBy(p => p.Int64PropertyId).SelectMany(s => s.Skip(1));
            if (dupeInt64s.Count() > 0)
            {
                isValid = false;
                dupeInt64s.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate Int64 property {dupe.Int64PropertyId} - you may only have one."));
            }

            var dupeDoubles = model.DoubleProperties.GroupBy(p => p.DoublePropertyId).SelectMany(p => p.Skip(1));
            if (dupeDoubles.Count() > 0)
            {
                isValid = false;
                dupeDoubles.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate Double property {dupe.DoublePropertyId} - you may only have one."));
            }

            var dupeStrings = model.StringProperties.GroupBy(p => p.StringPropertyId).SelectMany(p => p.Skip(1));
            if (dupeStrings.Count() > 0)
            {
                isValid = false;
                dupeStrings.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate String property {dupe.StringPropertyId} - you may only have one."));
            }

            var dupeIids = model.IidProperties.GroupBy(p => p.IidPropertyId).SelectMany(p => p.Skip(1));
            if (dupeIids.Count() > 0)
            {
                isValid = false;
                dupeIids.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate Instance ID property {dupe.IidPropertyId} - you may only have one."));
            }

            var dupeDids = model.DidProperties.GroupBy(p => p.DataIdPropertyId).SelectMany(p => p.Skip(1));
            if (dupeDids.Count() > 0)
            {
                isValid = false;
                dupeDids.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate Data ID property {dupe.DataIdPropertyId} - you may only have one."));
            }

            var dupeBools = model.BoolProperties.GroupBy(p => p.BoolPropertyId).SelectMany(p => p.Skip(1));
            if (dupeBools.Count() > 0)
            {
                isValid = false;
                dupeBools.ToList().ForEach(dupe => model.ErrorMessages.Add($"Duplicate Bool property {dupe.BoolPropertyId} - you may only have one."));
            }

            if (model.ItemType == null)
            {
                isValid = false;
                model.ErrorMessages.Add("Item Type is required.");
            }

            if (model.WeenieType == null || model.WeenieType == 0)
            {
                isValid = false;
                model.ErrorMessages.Add("Weenie Type is required.");
            }

            if (!ModelState.IsValid || !isValid)
            {
                isValid = false;
                foreach (ModelState state in ModelState.Values)
                {
                    foreach (ModelError error in state.Errors)
                    {
                        model.ErrorMessages.Add(error.ErrorMessage);
                    }
                }
            }

            if (!isValid)
                return View(model);

            return null;
        }

        private ActionResult HandlePostback(Weenie model)
        {
            model.CleanDeletedAndEmptyProperties();

            switch (model.MvcAction)
            {
                case WeenieCommands.AddIntProperty:
                    if (model.NewIntPropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.IntProperties.Add(new IntProperty() { IntPropertyId = (int)model.NewIntPropertyId.Value });

                    model.NewIntPropertyId = null;
                    break;

                case WeenieCommands.AddStringProperty:
                    if (model.NewStringPropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.StringProperties.Add(new StringProperty() { StringPropertyId = (int)model.NewStringPropertyId.Value });

                    model.NewStringPropertyId = null;
                    break;

                case WeenieCommands.AddInt64Property:
                    if (model.NewInt64PropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.Int64Properties.Add(new Int64Property() { Int64PropertyId = (int)model.NewInt64PropertyId.Value });

                    model.NewInt64PropertyId = null;
                    break;

                case WeenieCommands.AddDoubleProperty:
                    if (model.NewDoublePropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.DoubleProperties.Add(new DoubleProperty() { DoublePropertyId = (int)model.NewDoublePropertyId.Value });

                    model.NewDoublePropertyId = null;
                    break;

                case WeenieCommands.AddDidProperty:
                    if (model.NewDidPropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.DidProperties.Add(new DataIdProperty() { DataIdPropertyId = (int)model.NewDidPropertyId.Value });

                    model.NewDidPropertyId = null;
                    break;

                case WeenieCommands.AddIidProperty:
                    if (model.NewIidPropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.IidProperties.Add(new InstanceIdProperty() { IidPropertyId = (int)model.NewIidPropertyId.Value });

                    model.NewIidPropertyId = null;
                    break;

                case WeenieCommands.AddBoolProperty:
                    if (model.NewBoolPropertyId == null)
                        model.ErrorMessages.Add("You must select a Property to add.");
                    else
                        model.BoolProperties.Add(new BoolProperty() { BoolPropertyId = (int)model.NewBoolPropertyId.Value });

                    model.NewBoolPropertyId = null;
                    break;

                case WeenieCommands.AddSpell:
                    if (model.NewSpellId == null)
                        model.ErrorMessages.Add("You must select a Spell to add.");
                    else
                        model.Spells.Add(new Spell() { SpellId = (int)model.NewSpellId.Value });

                    model.NewSpellId = null;
                    break;

                case WeenieCommands.AddPosition:
                    if (model.NewPositionType == null)
                        model.ErrorMessages.Add("You must select a Position Type to add.");
                    else
                        model.Positions.Add(new Models.Shared.Position() { PositionType = (int)model.NewPositionType.Value });

                    break;

                case WeenieCommands.AddBookPage:
                    model.BookProperties.Add(new BookPage() { Page = (uint)model.BookProperties.Count });
                    break;

                case WeenieCommands.AddEmoteSet:
                    model.EmoteTable.Add(new EmoteSet() { EmoteCategoryId = (uint)model.NewEmoteCategory, EmoteSetGuid = Guid.NewGuid() });
                    model.NewEmoteCategory = EmoteCategory.Invalid;
                    break;

                case WeenieCommands.AddGeneratorTable:
                    model.GeneratorTable.Add(new Models.Shared.GeneratorTable());
                    break;

                case WeenieCommands.AddEmote:
                    EmoteSet emoteSet = model.EmoteTable.FirstOrDefault(es => es.EmoteSetGuid == model.EmoteSetGuid);

                    if (emoteSet == null)
                    {
                        model.ErrorMessages.Add("You must select an Emote Type to add.");
                    }
                    else
                    {
                        var order = emoteSet.Emotes.Max(e => e.SortOrder) + 1;
                        emoteSet.Emotes.Add(new Models.Shared.Emote() { EmoteSetGuid = emoteSet.EmoteSetGuid, EmoteType = emoteSet.NewEmoteType, EmoteGuid = Guid.NewGuid(), SortOrder = order });
                        emoteSet.NewEmoteType = EmoteType.Invalid;
                    }

                    break;

                case WeenieCommands.AddSkill:
                    if (model.NewSkillId == null)
                        model.ErrorMessages.Add("You must select a skill to add.");
                    else
                        model.Skills.Add(new Models.Shared.Skill() { SkillId = (int)model.NewSkillId.Value });
                    
                    break;

                case WeenieCommands.AddCreateItem:
                    model.CreateList.Add(new CreationProfile() { CreationProfileGuid = Guid.NewGuid(), OwnerId = model.DataObjectId });
                    break;

                case WeenieCommands.AddBodyParts:
                    model.BodyParts = new List<Models.Shared.BodyPart>
                    {
                        new Models.Shared.BodyPart() { BodyPartType = 0 }
                    };
                    break;

                case WeenieCommands.AddBodyPart:
                    model.BodyParts.Add(new Models.Shared.BodyPart() { BodyPartType = model.NewBodyPartType ?? BodyPartType.Head });
                    break;

                case null:
                    return null;
            }

            SortTheThings(model);
            model.MvcAction = null;
            model.NewPositionType = null;
            return View("Edit", model);
        }

        [HttpGet]
        public ActionResult WeenieFinder()
        {
            return View(new SearchWeeniesCriteria());
        }

        [HttpPost]
        public JsonResult WeenieFinder(SearchWeeniesCriteria model)
        {
            List<WeenieSearchResult> results = ContentProviderHost.CurrentProvider.WeenieSearch(GetUserToken(), model);
            return Json(results);
        }

        [HttpGet]
        [Authorize]
        public ActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        public ActionResult UploadEx()
        {
            string fileNameCopy = "n/a";

            try
            {
                foreach (string fileName in Request.Files)
                {
                    fileNameCopy = fileName;
                    HttpPostedFileBase file = Request.Files[fileName];
                    uint weenieId = 0;

                    using (MemoryStream memStream = new MemoryStream())
                    {
                        file.InputStream.CopyTo(memStream);
                        byte[] data = memStream.ToArray();

                        string serialized = Encoding.UTF8.GetString(data);
                        CachePwnWeenie pw = JsonConvert.DeserializeObject<CachePwnWeenie>(serialized);

                        List<string> messages = new List<string>();
                        Weenie w = pw.ConvertToWeenie(out messages);
                        ImportedWeenie = w;
                        weenieId = w.DataObjectId;
                    }

                    return Json(new { id = weenieId });
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error parsing uploaded weenie {fileNameCopy}.", ex);
            }

            return new EmptyResult();
        }

        [HttpGet]
        public ActionResult DownloadOriginalToPhatJson(uint id)
        {
            try
            {
                Weenie model = SandboxContentProviderHost.CurrentProvider.GetWeenie(GetUserToken(), id);
                CachePwnWeenie pwn = CachePwnWeenie.ConvertFromWeenie(model);
                JsonSerializerSettings s = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                string content = JsonConvert.SerializeObject(pwn, Formatting.None, s);
                string filename = model.StringProperties.First(p => p.StringPropertyId == (int)StringPropertyId.Name).Value + " (" + id.ToString() + ").json";
                return File(Encoding.UTF8.GetBytes(content), "application/json", filename);
            }
            catch (Exception ex)
            {
                log.Error($"Error exporting weenie {id}", ex);

                IndexModel model = new IndexModel();
                model.ErrorMessages.Add($"Error exporting weenie {id}");
                model.Exception = ex;
                CurrentIndexModel = model;

                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public ActionResult DownloadOriginalToDfJson(uint id)
        {
            try
            {
                Weenie model = SandboxContentProviderHost.CurrentProvider.GetWeenieFromSource(GetUserToken(), id);
                JsonSerializerSettings s = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                string content = JsonConvert.SerializeObject(model, Formatting.None, s);
                string filename = $"{id}.json";
                return File(Encoding.UTF8.GetBytes(content), "application/json", filename);
            }
            catch (Exception ex)
            {
                log.Error($"Error exporting weenie {id}", ex);

                IndexModel model = new IndexModel();
                model.ErrorMessages.Add($"Error exporting weenie {id}");
                model.Exception = ex;
                CurrentIndexModel = model;

                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        [Authorize]
        public ActionResult Sandbox()
        {
            SandboxModel model = new SandboxModel();
            BaseModel.CopyBaseData(CurrentBaseModel, model);

            model.Changes = SandboxContentProviderHost.CurrentProvider.GetMyWeenieChanges(GetUserToken());

            return View(model);
        }

        [HttpGet]
        [Authorize(Roles = "Developer")]
        public ActionResult Submissions()
        {
            SandboxModel model = new SandboxModel();
            BaseModel current = CurrentBaseModel;
            BaseModel.CopyBaseData(current, model);
            CurrentBaseModel = current;

            if (User.IsInRole("Developer"))
            {
                List<WeenieChange> temp = SandboxContentProviderHost.CurrentProvider.GetAllWeenieChanges();
                model.Changes = temp.Where(x => x.Submitted).ToList();
            }
            else
                model.Changes = SandboxContentProviderHost.CurrentProvider.GetMyWeenieChanges(GetUserToken());

            return View(model);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public ActionResult Delete(uint id)
        {
            Weenie model = SandboxContentProviderHost.CurrentProvider.GetWeenie(GetUserToken(), id);
            if (model == null)
                return RedirectToAction("Index");

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public ActionResult Delete(Weenie model)
        {
            try
            {
                if (model.MvcAction == WeenieCommands.Delete)
                {
                    IndexModel indexModel = new IndexModel();
                    SandboxContentProviderHost.CurrentProvider.DeleteWeenie(GetUserToken(), model.WeenieClassId);
                    indexModel.SuccessMessages.Add("Weenie " + model.WeenieClassId.ToString() + " successfully deleted.");
                    CurrentIndexModel = indexModel;
                }
            }
            catch (Exception ex)
            {
                model.ErrorMessages.Add("Error saving weenie in the API");
                model.Exception = ex;
                return View(model);
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        [Authorize]
        public ActionResult DownloadSandboxToPhatJson(uint id, string userGuid)
        {
            WeenieChange wc = null;

            if (string.IsNullOrWhiteSpace(userGuid))
            {
                // no userGuid specified, assume your own sandbox
                wc = SandboxContentProviderHost.CurrentProvider.GetMySandboxedChange(GetUserToken(), id);
            }
            else
            {
                // validate dev/admin or matches your id
                if (User.IsInRole("Developer") || GetUserGuid() == userGuid)
                    wc = SandboxContentProviderHost.CurrentProvider.GetSandboxedChange(Guid.Parse(userGuid), id);
                else
                    return new HttpNotFoundResult();
            }

            if (wc == null)
                return new HttpNotFoundResult();

            CachePwnWeenie pwn = CachePwnWeenie.ConvertFromWeenie(wc.Weenie);
            JsonSerializerSettings s = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string content = JsonConvert.SerializeObject(pwn, Formatting.None, s);
            string filename = wc.Weenie.StringProperties.First(p => p.StringPropertyId == (int)StringPropertyId.Name).Value + $" ({id}).json";
            return File(Encoding.UTF8.GetBytes(content), "application/json", filename);
        }

        [HttpGet]
        [Authorize]
        public ActionResult DownloadSandboxToDfJson(uint id, string userGuid)
        {
            WeenieChange wc = null;

            if (string.IsNullOrWhiteSpace(userGuid))
            {
                // no userGuid specified, assume your own sandbox
                wc = SandboxContentProviderHost.CurrentProvider.GetMySandboxedChange(GetUserToken(), id);
            }
            else
            {
                // validate dev/admin or matches your id
                if (User.IsInRole("Developer") || GetUserGuid() == userGuid)
                    wc = SandboxContentProviderHost.CurrentProvider.GetSandboxedChange(Guid.Parse(userGuid), id);
                else
                    return new HttpNotFoundResult();
            }

            if (wc == null)
                return new HttpNotFoundResult();

            JsonSerializerSettings s = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string content = JsonConvert.SerializeObject(wc.Weenie, Formatting.None, s);
            string filename = $"{id}.json";
            return File(Encoding.UTF8.GetBytes(content), "application/json", filename);
        }

        [HttpPost]
        [Authorize]
        public ActionResult SelectDfServer(DownloadChangeModel model)
        {
            CurrentDownload = model;

            if (model.TargetServerGuid == null)
                return RedirectToAction("SelectManagedWorld");

            ManagedServerModel managedWorld = CurrentUser.ManagedWorlds.FirstOrDefault(w => w.ManagedServerGuid.ToString() == model.TargetServerGuid);
            if (managedWorld == null)
                return RedirectToAction("SelectManagedWorld");

            ApiAccountModel currentUser = CurrentUser;
            currentUser.SelectedManagedWorldGuid = managedWorld.ManagedServerGuid.Value;
            currentUser.SelectedManagedWorld = managedWorld.ServerName;
            CurrentUser = currentUser;

            if (string.IsNullOrWhiteSpace(managedWorld.CachedToken))
                return RedirectToAction("LogIntoManagedWorld");

            return RedirectToAction("SendToDfServer");
        }

        [HttpGet]
        [Authorize]
        public ActionResult LogIntoManagedWorld()
        {
            DownloadChangeModel model = CurrentDownload;
            return View(model);
        }

        [HttpPost]
        [Authorize]
        public ActionResult LogIntoManagedWorld(DownloadChangeModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.TargetServerUsername) || string.IsNullOrWhiteSpace(model?.TargetServerPassword))
            {
                model.ErrorMessages.Add("Username and password are required.");
                return View(model);
            }

            ApiAccountModel user = CurrentUser;
            ManagedServerModel managedWorld = user.ManagedWorlds.FirstOrDefault(w => w.ManagedServerGuid.ToString() == model.TargetServerGuid);
            if (managedWorld == null)
                return RedirectToAction("SelectManagedWorld");

            string token;

            try
            {
                // attempt to log into the managed world
                token = ContentProviderHost.ManagedWorldProvider.Authenticate(managedWorld.Address, model.TargetServerUsername, model.TargetServerPassword);
            }
            catch (Exception ex)
            {
                model.ErrorMessages.Add(ex.Message);
                return View(model);
            }

            managedWorld.CachedToken = token;

            // because pointers are fun, we can now update the current user with the user we pulled out earlier
            CurrentUser = user;

            return RedirectToAction("SendChangeToServer");
        }

        [HttpGet]
        [Authorize]
        public ActionResult SendToDfServer(uint id, string userGuid, bool preview = false)
        {
            CurrentDownload = new DownloadChangeModel() { WeenieId = id, FromUserGuid = userGuid, PreviewOnly = preview };
            return RedirectToAction("SendChangeToServer");
        }

        [HttpGet]
        [Authorize]
        public ActionResult SelectManagedWorld()
        {
            DownloadChangeModel model = CurrentDownload;
            return View(model);
        }

        [HttpGet]
        [Authorize]
        public ActionResult SendChangeToServer()
        {
            DownloadChangeModel model = CurrentDownload;
            WeenieChange wc = SandboxContentProviderHost.CurrentProvider.GetSandboxedChange(Guid.Parse(model.FromUserGuid), model.WeenieId);

            if (wc == null)
                return RedirectToAction("Sandbox");

            ApiAccountModel user = CurrentUser;
            if (user.SelectedManagedWorldGuid == null)
            {
                if (user.ManagedWorlds?.Count == 1)
                {
                    // use the only server they have
                    model.TargetServerGuid = user.ManagedWorlds[0].ManagedServerGuid.ToString();
                }
                else
                {
                    // user needs to select a managed server
                    return RedirectToAction("SelectManagedWorld");
                }
            }
            else
            {
                model.TargetServerGuid = user.SelectedManagedWorldGuid.ToString();
            }

            CurrentDownload = model;

            ManagedServerModel managedWorld = user.ManagedWorlds.FirstOrDefault(w => w.ManagedServerGuid.ToString() == model.TargetServerGuid);
            if (managedWorld == null)
                return RedirectToAction("SelectManagedWorld");

            if (string.IsNullOrWhiteSpace(managedWorld.CachedToken))
                return RedirectToAction("LogIntoManagedWorld");

            IRestResponse restResponse;
            if (model.PreviewOnly)
            {
                wc.Weenie.ReplaceLiveObjects = true;
                restResponse = ContentProviderHost.ManagedWorldProvider.PreviewWeenie(managedWorld.Address, managedWorld.CachedToken, wc.Weenie);
            }
            else
            {
                restResponse = ContentProviderHost.ManagedWorldProvider.UpdateWeenie(managedWorld.Address, managedWorld.CachedToken, wc.Weenie);
            }

            if (restResponse.StatusCode == System.Net.HttpStatusCode.OK)
            {
                BaseModel baseModel = CurrentBaseModel ?? new BaseModel();
                baseModel.SuccessMessages.Add("Weenie sent to server.");
                CurrentBaseModel = baseModel;
                return RedirectToAction("Sandbox");
            }

            if (restResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                model.ErrorMessages.Add("Authorization denied.");
                CurrentDownload = model;
                return RedirectToAction("LogIntoManagedWorld");
            }

            model.InfoMessages.Add($"Unexpected result from server: {restResponse.StatusCode} - {restResponse.Content}");
            CurrentDownload = model;
            return RedirectToAction("LogIntoManagedWorld");
        }

        [HttpGet]
        [Authorize]
        public ActionResult WithdrawSubmission(uint id)
        {
            List<WeenieChange> data = SandboxContentProviderHost.CurrentProvider.GetMyWeenieChanges(GetUserToken());

            WeenieChange theOne = data.FirstOrDefault(wc => wc.Weenie.WeenieClassId == id);
            if (theOne != null)
            {
                theOne.Submitted = false;
                SandboxContentProviderHost.CurrentProvider.UpdateWeenieChange(GetUserGuid(), theOne);
            }

            return RedirectToAction("Sandbox");
        }

        [HttpGet]
        [Authorize]
        public ActionResult CreateSubmission(uint id)
        {
            List<WeenieChange> data = SandboxContentProviderHost.CurrentProvider.GetMyWeenieChanges(GetUserToken());

            WeenieChange theOne = data.FirstOrDefault(wc => wc.Weenie.WeenieClassId == id);
            if (theOne != null)
            {
                theOne.Submitted = true;
                SandboxContentProviderHost.CurrentProvider.UpdateWeenieChange(GetUserGuid(), theOne);

                WeenieSubmissionEvent wse = new WeenieSubmissionEvent();
                wse.SubmittingUserGuid = theOne.UserGuid;
                wse.ChangelogComment = theOne.Weenie.UserChangeSummary;
                wse.SubmittingUser = CurrentUser.DisplayName;
                wse.WeenieId = theOne.Weenie.DataObjectId;
                wse.WeenieName = theOne.Weenie.Name;
                wse.SubmissionTime = DateTimeOffset.Now;
                DiscordController.PostToDiscordAsync(wse);
            }

            return RedirectToAction("Sandbox");
        }

        [HttpGet]
        [Authorize]
        public ActionResult DeleteSandboxWeenie(uint id)
        {
            List<WeenieChange> data = SandboxContentProviderHost.CurrentProvider.GetMyWeenieChanges(GetUserToken());

            WeenieChange theOne = data.FirstOrDefault(wc => wc.Weenie.WeenieClassId == id);
            if (theOne != null)
                SandboxContentProviderHost.CurrentProvider.DeleteWeenieChange(theOne.UserGuid, theOne);

            return RedirectToAction("Sandbox");
        }

        [HttpGet]
        [Authorize(Roles = "Developer")]
        public ActionResult AcceptChange(uint id, string userGuid)
        {
            List<WeenieChange> temp = SandboxContentProviderHost.CurrentProvider.GetAllWeenieChanges();
            WeenieChange change = temp.FirstOrDefault(x => x.Submitted && x.UserGuid == userGuid && x.Weenie.WeenieClassId == id);

            BaseModel baseModel = CurrentBaseModel ?? new BaseModel();

            if (change != null)
            {
                // apply the changelog
                if (!string.IsNullOrWhiteSpace(change.Weenie.UserChangeSummary))
                {
                    change.Weenie.Changelog.Add(new ChangelogEntry()
                    {
                        Author = change.UserName,
                        Created = change.SubmissionTime,
                        Comment = change.Weenie.UserChangeSummary
                    });
                }

                // this goes to DF.com, and requires the current token, not a substituted one
                SandboxContentProviderHost.CurrentProvider.UpdateWeenieInSource(GetUserToken(), change.Weenie);
                baseModel.SuccessMessages.Add("Weenie updated.");
                CurrentBaseModel = baseModel;

                WeenieAcceptanceEvent wae = new WeenieAcceptanceEvent();
                wae.AcceptanceTime = DateTimeOffset.Now;
                wae.AcceptingUser = CurrentUser.DisplayName;
                wae.ChangelogComment = change.Weenie.UserChangeSummary;
                wae.SubmittingUser = change.UserName;
                wae.WeenieId = id;
                wae.WeenieName = change.Weenie.Name;
                DiscordController.PostToDiscordAsync(wae);

                // delete the change request
                SandboxContentProviderHost.CurrentProvider.DeleteWeenieChange(change.UserGuid, change);
            }

            return RedirectToAction("Submissions");
        }

        [HttpPost]
        [Authorize(Roles = "Developer")]
        public ActionResult RejectChange(string userGuid, int weenieClassId, string rejectionComment)
        {
            List<WeenieChange> temp = SandboxContentProviderHost.CurrentProvider.GetAllWeenieChanges();
            WeenieChange change = temp.FirstOrDefault(x => x.Submitted && x.UserGuid == userGuid && x.Weenie.WeenieClassId == weenieClassId);

            BaseModel baseModel = CurrentBaseModel ?? new BaseModel();

            if (change != null)
            {
                change.Submitted = false;
                change.Discussion.Add(new ChangeDiscussionEntry()
                {
                    Comment = rejectionComment,
                    Created = DateTime.Now,
                    UserGuid = Guid.Parse(GetUserGuid()),
                    UserName = GetUserName()
                });

                SandboxContentProviderHost.CurrentProvider.UpdateWeenieChange(change.UserGuid, change);
                baseModel.SuccessMessages.Add("Weenie updated.");
                CurrentBaseModel = baseModel;
            }

            return RedirectToAction("Submissions");
        }

        [HttpPost]
        [Authorize]
        public ActionResult AddDiscussionComment(string userGuid, int weenieClassId, string discussionComment, string source)
        {
            string currentUser = GetUserGuid();

            if (!(User.IsInRole("Developer") || userGuid == currentUser))
            {
                // only the submitter and developers can comment
                return RedirectToAction(source);
            }

            List<WeenieChange> temp = SandboxContentProviderHost.CurrentProvider.GetAllWeenieChanges();
            WeenieChange change = temp.FirstOrDefault(x => x.UserGuid == userGuid && x.Weenie.WeenieClassId == weenieClassId);

            if (change == null)
                return RedirectToAction(source);

            change.Discussion.Add(new ChangeDiscussionEntry()
            {
                Comment = discussionComment,
                Created = DateTime.Now,
                UserName = GetUserName(),
                UserGuid = Guid.Parse(GetUserGuid())
            });

            SandboxContentProviderHost.CurrentProvider.UpdateWeenieChange(userGuid, change);

            return RedirectToAction(source);
        }
    }
}
