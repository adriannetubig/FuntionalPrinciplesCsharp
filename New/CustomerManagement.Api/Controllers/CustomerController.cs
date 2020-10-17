﻿using System.Net.Http;
using System.Web.Http;

using CSharpFunctionalExtensions;

using CustomerManagement.Api.Models;
using CustomerManagement.Logic.Model;
using CustomerManagement.Logic.Utils;


namespace CustomerManagement.Api.Controllers
{
    public class CustomerController : Controller
    {
        private readonly CustomerRepository _customerRepository;
        private readonly IEmailGateway _emailGateway;

        public CustomerController(UnitOfWork unitOfWork, IEmailGateway emailGateway)
            : base(unitOfWork)
        {
            _customerRepository = new CustomerRepository(unitOfWork);
            _emailGateway = emailGateway;
        }

        [HttpPost]
        [Route("customers")]
        public HttpResponseMessage Create(CreateCustomerModel model)
        {
            Result<CustomerName> customerName = CustomerName.Create(model.Name);
            Result<Email> primaryEmail = Email.Create(model.PrimaryEmail);
            Result<Maybe<Email>> secondaryEmail = GetSecondaryEmail(model.SecondaryEmail);
            Result<Industry> industry = Industry.Get(model.Industry);

            Result result = Result.Combine(customerName, primaryEmail, secondaryEmail, industry);
            if (result.IsFailure)
                return Error(result.Error);

            var customer = new Customer(customerName.Value, primaryEmail.Value, secondaryEmail.Value, industry.Value);
            _customerRepository.Save(customer);

            return Ok();
        }

        private Result<Maybe<Email>> GetSecondaryEmail(string secondaryEmail)
        {
            if (secondaryEmail == null)
                return Maybe<Email>.None;

            return Email.Create(secondaryEmail)
                .Map(email => (Maybe<Email>)email);
        }

        [HttpGet]
        [Route("customers/{id}")]
        public HttpResponseMessage Get(long id)
        {
            Maybe<Customer> customerOrNothing = _customerRepository.GetById(id);
            if (customerOrNothing.HasNoValue)
                return Error("Customer with such Id is not found: " + id);

            Customer customer = customerOrNothing.Value;

            var model = new
            {
                customer.Id,
                Name = customer.Name.Value,
                PrimaryEmail = customer.PrimaryEmail.Value,
                SecondaryEmail = customer.SecondaryEmail.HasValue ? customer.SecondaryEmail.Value.Value : null,
                Industry = customer.EmailingSettings.Industry.Name,
                customer.EmailingSettings.EmailCampaign,
                customer.Status
            };

            return Ok(model);
        }

        [HttpDelete]
        [Route("customers/{id}/emailing")]
        public HttpResponseMessage DisableEmailing(long id)
        {
            Maybe<Customer> customerOrNothing = _customerRepository.GetById(id);
            if (customerOrNothing.HasNoValue)
                return Error("Customer with such Id is not found: " + id);

            Customer customer = customerOrNothing.Value;
            customer.DisableEmailing();

            return Ok();
        }

        [HttpPut]
        [Route("customers/{id}")]
        public HttpResponseMessage Update(UpdateCustomerModel model)
        {
            Result<Customer> customerResult = _customerRepository.GetById(model.Id)
                .ToResult("Customer with such Id is not found: " + model.Id);
            Result<Industry> industryResult = Industry.Get(model.Industry);

            return Result.Combine(customerResult, industryResult)
                .Tap(() => customerResult.Value.UpdateIndustry(industryResult.Value))
                .Finally(result => result.IsSuccess ? Ok() : Error(result.Error));
        }

        [HttpPost]
        [Route("customers/{id}/promotion")]
        public HttpResponseMessage Promote(long id)
        {
            return _customerRepository.GetById(id)
                .ToResult("Customer with such Id is not found: " + id)
                .Ensure(customer => customer.CanBePromoted(), "The customer has the highest status possible")
                .Tap(customer => customer.Promote())
                .Tap(customer => _emailGateway.SendPromotionNotification(customer.PrimaryEmail, customer.Status))
                .Finally(result => result.IsSuccess ? Ok() : Error(result.Error));
        }
    }
}
