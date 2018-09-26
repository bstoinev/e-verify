using LookoutService.Model.Dto;
using LookoutService.Web.Services.NonFederalWorkingDayManagement;
using LookoutService.Web.ViewModel.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace LookoutService.Web.Controllers
{
    public class NonFederalWorkingDayController : BaseController
    {
        private readonly INonFederalWorkingDayManagementService _nonFederalWorkingDayService = null;

        public NonFederalWorkingDayController(INonFederalWorkingDayManagementService nonFederalWorkingDayService)
        {
            _nonFederalWorkingDayService = nonFederalWorkingDayService;
        }

        private JsonResultModel<IEnumerable<NonFederalWorkingDayDto>> InvokeService(Func<IEnumerable<NonFederalWorkingDayDto>, I9UserDto, IEnumerable<NonFederalWorkingDayDto>> operation, IEnumerable<NonFederalWorkingDayDto> models)
        {
            var result = new JsonResultModel<IEnumerable<NonFederalWorkingDayDto>>();

            if (ModelState.IsValid)
            {
                var modifier = new I9UserDto
                {
                    Id = SiteContext.User.UserId
                };

                try
                {
                    result.Model = operation(models, modifier);
                    result.Message = "Operation completed successfully.";
                }
                catch (Exception ex)
                {
                    result.HasError = true;
#if DEBUG
                    result.Message = string.Concat("Operation failed: ", ex.Message);
#else
                    result.Message = "Operation failed: Internal server error.";
#endif
                }

                if (result.Model != null)
                {
                    foreach (var dto in result.Model)
                    {
                        dto.CreatedOn = null;
                        dto.Creator = null;
                        dto.ModifiedOn = null;
                        dto.Modifier = null;
                    }
                }
            }
            else
            {
                result.HasError = true;
                result.Message = "Invalid data.";
            }

            return result;
        }

        public ActionResult Index()
        {
            var model = _nonFederalWorkingDayService.Load();
            
            return View(model);
        }

        [HttpPost]
        public ActionResult Create(IEnumerable<NonFederalWorkingDayDto> models)
        {
            var result = InvokeService(_nonFederalWorkingDayService.Create, models);

            return Json(result);
        }

        [HttpPost]
        public ActionResult Update(IEnumerable<NonFederalWorkingDayDto> models)
        {
            var result = InvokeService(_nonFederalWorkingDayService.Update, models);

            return Json(result);
        }

        [HttpPost]
        public ActionResult Delete(IEnumerable<NonFederalWorkingDayDto> models)
        {
            var result = InvokeService(_nonFederalWorkingDayService.Delete, models);

            if (!result.HasError)
            {
                var plurality = models.Count() == 1 ? "Entity" : "Entities";
                result.Message = string.Concat(plurality, " deleted successfully.");
            }

            return Json(result);
        }
    }
}
