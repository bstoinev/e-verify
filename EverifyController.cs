using LookoutService.EVerify.Data.Request;
using LookoutService.EVerify.Data.Response;
using LookoutService.Model.BusinessInteligence;
using LookoutService.Model.BusinessInteligence.Everify;
using LookoutService.Model.Dto.Everify;
using LookoutService.Web.Models.Everify;
using LookoutService.Web.Services.EVerifyManagement.ServiceContracts;
using LookoutService.Web.Services.IcaManagement;
using LookoutService.Web.ViewModel.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace LookoutService.Web.Controllers
{
    public class EverifyController : BaseController
    {
        private readonly IEVerifyManagementService evManagement = null;
        private readonly IIcaDataManagementService icaManagement = null;

        public EverifyController(IEVerifyManagementService everifyManagementService, IIcaDataManagementService icaDataManagementService)
        {
            evManagement = everifyManagementService;
            icaManagement = icaDataManagementService;
        }

        public JsonResult TncDropContest(EverifyCaseDto EverifyCase)
        {
            var result = new JsonResultModel<EverifyCaseJobDto>();

            try
            {
                result.Model = evManagement.TncDropContest(EverifyCase, SiteContext.User.UserId);
                result.Message = "Contest disclaimer recorded.";
            }
            catch (Exception ex)
            {
                result.HasError = true;
                result.Message = string.Concat("Error occurred: ", ex.Message);
                Logger.Error("TncDropContest failed with the following exception:", ex);
            }
            
            return Json(result);
        }

        public ActionResult CaseProcessingIneligible()
        {
            return PartialView("~/Views/Everify/_CaseProcessing-Ineligible.cshtml");
        }

        public ActionResult CaseProcessingPending()
        {
            return PartialView("~/Views/Everify/_CaseProcessing-Pending.cshtml");
        }

        public ActionResult CaseProcessingUnqualified()
        {
            return PartialView("~/Views/Everify/_CaseProcessing-Unqualified.cshtml");
        }

        public ActionResult QueryCaseHistory(EverifyCaseDto dto)
        {
            if (dto == null || !dto.Id.HasValue)
            {
                throw new ArgumentException("Case could not be determined.");
            }

            var criterionDto = new EverifyCaseJobDto
            {
                Case = new EverifyCaseDto { Id = dto.Id }
            };

            var jobs = evManagement.LoadJobs(criterionDto);

            var vm = new EverifyCaseHistoryModel
            {
                Events = jobs.Select(j => EverifyCaseHistoryRecord.FromJob(j))
            };

            return PartialView("~/Views/Everify/_CaseHistory.cshtml", vm);
        }

        public ActionResult QueryCaseProcessing(EverifyCaseDto dto)
        {
            var existingCases = evManagement.LoadEverifyCases(dto);
            var activeCase = (IEverifyCase)existingCases.FirstOrDefault(ec => !ec.State.Value.HasFlag(EverifyCaseState.Closed));
            
            var viewName = string.Empty;

            if (activeCase == null)
            {
                throw new ArgumentException("Invalid E-Verify case specified.");
            }

            var mrj = activeCase.Jobs.LastOrDefault();
            var currentState = activeCase.State;

            if (currentState == EverifyCaseState.Pending)
            {
                viewName = "_CaseProcessing-Pending.cshtml";
            }
            else if (currentState.HasFlag(EverifyCaseState.SsaTnc))
            {
                var processState = TncState.Unknown;
                var lastTncJob = mrj;

                if (lastTncJob == null)
                {

                }
                else if (lastTncJob.JobType == EverifyCaseJobType.SsaReferral)
                {
                    var referralRes = JsonConvert.DeserializeObject<SsaReferralResponse>(lastTncJob.Response);

                    processState |= referralRes != null && referralRes.StatusCode == 0 ? TncState.ReferralInitiated : TncState.Error;
                }
                else if (lastTncJob.JobType == EverifyCaseJobType.SsaNotify)
                {
                    var notificationReq = JsonConvert.DeserializeObject<TncNotificationRequest>(lastTncJob.Request);
                    var notificationRes = JsonConvert.DeserializeObject<TncNotificationResponse>(lastTncJob.Response);

                    processState |= notificationReq.EmployeeNotified ? TncState.NotificationSucceeded : TncState.NotificationFailed;

                    if (notificationRes == null || notificationRes.StatusCode != 0)
                    {
                        processState |= TncState.Error;
                    }
                }

                ViewBag.TncProcessState = processState;
                viewName = "_CaseProcessing-TNC.cshtml";
            }
            else if (currentState.HasFlag(EverifyCaseState.InitialVerification))
            {
                var verificationJob = activeCase.Jobs.LastOrDefault(j => j.JobType == EverifyCaseJobType.InitialVerification);
                var verificationRes = JsonConvert.DeserializeObject<VerificationResponse>(verificationJob.Response ?? string.Empty);

                if (currentState.HasFlag(EverifyCaseState.Error))
                {
                    viewName = "_CaseProcessing-VerificationFailed.cshtml";

                    if (verificationRes == null)
                    {
                        ViewBag.ProcessingAssistanceHtml = verificationJob.Response;
                    }
                    else
                    {
                        ViewBag.ProcessingAssistanceHtml = string.Format("Error code: {0}<br />Error message: {1}", verificationRes.StatusCode, verificationRes.StatusMessage);
                    }
                }
                else if (currentState.HasFlag(EverifyCaseState.EmploymentAuthorized))
                {
                    viewName = "_CaseProcessing-EmploymentAuthorized.cshtml";
                }
                else if (currentState.HasFlag(EverifyCaseState.SsaCaseIncomplete))
                {
                    ViewBag.EligibilityStatementMessage = verificationRes.EligibilityStatementMessage;
                    ViewBag.EligibilityStatementDescription = verificationRes.EligibilityStatementDescription;

                    viewName = "_CaseProcessing-CaseIncomplete.cshtml";
                }
                else if (currentState == EverifyCaseState.PhotoConfirmationRequired)
                {
                    viewName = "_CaseProcessing-PhotoMatch.cshtml";
                }
                else if (currentState.HasFlag(EverifyCaseState.SsaTnc) || currentState.HasFlag(EverifyCaseState.DhsTnc))
                {
                    viewName = "_CaseProcessing-TNC.cshtml";
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException("Unknown E-Verify case state: {0}", currentState.ToString());
            }
            
            return PartialView(string.Concat("~/Views/Everify/", viewName));
        }

        [HttpPost]
        public ActionResult SsaReverify(EverifyCaseDto EverifyCase)
        {
            var result = new JsonResultModel<EverifyCaseJobDto>();

            try
            {
                result.Model = evManagement.TriggerSsaReverification(EverifyCase, SiteContext.User.UserId);
            }
            catch (Exception ex)
            {
                result.HasError = true;
                result.Message = string.Concat("Error occurred: ", ex.Message);
                Logger.Error("SsaReverify failed with the following exception:", ex);
            }

            return Json(result);
        }

        [HttpPost]
        public ActionResult SsaTncEmployeeNotificationFailed(EverifyCaseDto EverifyCase)
        {
            var result = new JsonResultModel<EverifyCaseJobDto>();

            try
            {
                result.Model = evManagement.SsaTncEmployeeNotificationFailed(EverifyCase, SiteContext.User.UserId);
            }
            catch (Exception ex)
            {
                result.HasError = true;
                result.Message = string.Concat("Error occurred: ", ex.Message);
                Logger.Error("SsaNotificationFailed failed with the following exception:", ex);
            }

            return Json(result);
        }

        [HttpPost]
        public ActionResult SsaTncEmployeeNotificationSucceeded(EverifyCaseDto EverifyCase)
        {
            var result = new JsonResultModel<EverifyCaseJobDto>();

            try
            {
                result.Model = evManagement.SsaTncEmployeeNotificationSucceeded(EverifyCase, SiteContext.User.UserId);
            }
            catch (Exception ex)
            {
                result.HasError = true;
                result.Message = string.Concat("Error occurred: ", ex.Message);
                Logger.Error("SsatncEmployeeNotificationSucceeded failed with the following exception:", ex);
            }

            return Json(result);
        }

        [HttpPost]
        public JsonResult TriggerSsaReferral(EverifyCaseDto EverifyCase)
        {
            var result = new JsonResultModel<EverifyCaseJobDto>();

            try
            {
                result.Model = evManagement.TriggerSsaReferral(EverifyCase, SiteContext.User.UserId);
                result.Message = "Referral initiated.";
            }
            catch (Exception ex)
            {
                result.HasError = true;
                result.Message = string.Concat("Error occurred: ", ex.Message);
                Logger.Error("TriggerSsaReferral failed with the following exception:", ex);
            }

            return Json(result);
        }

        

        [HttpPost]
        public ActionResult TriggerVerification(EverifyCaseDto EverifyCase)
        {
            evManagement.TriggerVerification(EverifyCase, SiteContext.User.UserId);

            return RedirectToAction("QueryCaseProcessing", EverifyCase);
        }
    }
}
