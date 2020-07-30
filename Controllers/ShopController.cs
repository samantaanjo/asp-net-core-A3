using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeorgianComputers.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe;

namespace GeorgianComputers.Controllers
{
    public class ShopController : Controller
    {
        private readonly GeorgianComputersContext _context;

        //Add controler config .json
        private IConfiguration _configuration;

        public ShopController(GeorgianComputersContext context, IConfiguration configuration)
        {
            // instance of DBconnection class
            _context = context;
            _configuration = configuration; //Access to the API keys
        }

        public IActionResult Index()
        {
            var categories = _context.Category.OrderBy(c => c.Name).ToList();
            return View(categories);
        }

        //Browse 
        public IActionResult Browse(string category)
        {
            ViewBag.Category = category;

            var products = _context.Product.Where(p => p.Category.Name == category).OrderBy(p => p.Name).ToList();
            return View(products);
        }

        //Product Details
        public IActionResult ProductDetails(string product)
        {

            var selectedProduct = _context.Product.SingleOrDefault(p => p.Name == product);
            return View(selectedProduct);
        }

        //Post AddtoCart
        [HttpPost]
        [ValidateAntiForgeryToken]

        public IActionResult AddtoCart(int quantity, int ProductId)
        {
            var product = _context.Product.SingleOrDefault(p => p.ProductId == ProductId);
            var price = product.Price;

            var cartUsername = GetCartUserName();

            var cartItem = _context.Cart.SingleOrDefault(c => c.ProductId == ProductId && c.Username == cartUsername);

            if (cartItem == null)
            {

                var cart = new Cart
                {
                    ProductId = ProductId,
                    Quantity = quantity,
                    Price = price,
                    Username = cartUsername

                };
                _context.Cart.Add(cart);

            }
            else
            {
                cartItem.Quantity += quantity;
                _context.Update(cartItem);
            }



            _context.SaveChanges();
            return RedirectToAction("Cart");

        }

        private string GetCartUserName()
        {

            if (HttpContext.Session.GetString("CartUserName") == null)
            {

                var cartUsername = "";

                if (User.Identity.IsAuthenticated)
                {
                    cartUsername = User.Identity.Name;
                }
                else
                {
                    cartUsername = Guid.NewGuid().ToString();
                }

                HttpContext.Session.SetString("CartUserName", cartUsername);

            }

            return HttpContext.Session.GetString("CartUserName");
        }

        public IActionResult Cart()
        {


            var cartUsername = GetCartUserName();

            var cartItems = _context.Cart.Include(c => c.Product).Where(c => c.Username == cartUsername).ToList();
            return View(cartItems);
        }

        public IActionResult RemoveFromCart(int id)
        {
            var cartItem = _context.Cart.SingleOrDefault(c => c.CartId == id);

            _context.Cart.Remove(cartItem);

            _context.SaveChanges();

            return RedirectToAction("Cart");
        }

        [Authorize]
        public IActionResult Checkout()
        {

            MigrateCart();
            return View();
        }


        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Checkout([Bind("FirstName,LastName,Address,City,Province,PostalCode,Phone")] Models.Order order) //get
        {
            order.OrderDate = DateTime.Now;
            order.UserId = User.Identity.Name;

            var cartItems = _context.Cart.Where(c => c.Username == User.Identity.Name);
            decimal cartTotal = (from c in cartItems
                                 select c.Quantity * c.Price).Sum();
            order.Total = cartTotal;

            //Call the external SessionExtention
             HttpContext.Session.SetObject("Order", order);//orginal his code

            // HttpContext.Session.SetObject("Order", order.Total);
            //HttpContext.Session.SetObject("Order", cartTotal);

            //HttpContext.Session.SetString("cartTotal", cartTotal.ToString());


            return RedirectToAction("Payment");
        }


        private void MigrateCart()
        {
            if (HttpContext.Session.GetString("CartUsername") != User.Identity.Name)
            {
                var cartUsername = HttpContext.Session.GetString("Cartusername");

                var cartItems = _context.Cart.Where(c => c.Username == cartUsername);

                foreach (var item in cartItems)
                {
                    item.Username = User.Identity.Name;
                    _context.Update(item);

                }
                _context.SaveChanges();


            }
            HttpContext.Session.SetString("CartUsename", User.Identity.Name);
        }

        public IActionResult Payment()
        {
            var order = HttpContext.Session.GetObject<Models.Order>("Order");

           

            ViewBag.Total = order.Total;

            ViewBag.CentsTotal = order.Total * 100; // Stripe condition

            ViewBag.PublishableKey = _configuration.GetSection("Stripe")["PublishableKey"];

            return View();
        }

        //Get for authorization
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
         public IActionResult Payment( string stripeEmail, string stripeToken)
         {
             StripeConfiguration.ApiKey = _configuration.GetSection("Stripe")["SecretKey"];
             var cartUsername = HttpContext.Session.GetString("CartUsername");
             var cartItems = _context.Cart.Where(c => c.Username == cartUsername);
             var order = HttpContext.Session.GetObject<Models.Order>("Order");

             var customerService = new CustomerService();
             var chargeServices = new ChargeService();
             var customer = customerService.Create(new CustomerCreateOptions
             {
                 Email = stripeEmail,
                 Source = stripeToken

             });

             var charge = chargeServices.Create(new ChargeCreateOptions
             {

                 Amount = Convert.ToInt32(order.Total *100),
                 Description = "Pet Care Purchase",
                 Currency = "cad",
                 Customer = customer.Id

             });

             _context.Order.Add(order);
             _context.SaveChanges();

             foreach( var item in cartItems)
             {
                 var orderDetail = new OrderDetail
                 {
                     OrderId = order.OrderId,
                     ProductId = item.ProductId,
                     Quantity = item.Quantity,
                     Price = item.Price

                 };
                 _context.OrderDetail.Add(orderDetail);
             }
             _context.SaveChanges();

             //delee the cart
             foreach( var item in cartItems)
             {

                 _context.Cart.Remove(item);
             }

             _context.SaveChanges();

             return RedirectToAction("Details", "Orders", new {id = order.OrderId});
         } 

    
    }
}