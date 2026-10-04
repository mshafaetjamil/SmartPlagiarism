using Microsoft.AspNetCore.Mvc;

namespace SmartPlagiarism.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult About()
    {
        return View();
    }
}
